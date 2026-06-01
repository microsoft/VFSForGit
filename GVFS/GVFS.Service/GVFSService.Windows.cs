using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;

namespace GVFS.Service
{
    public class GVFSService : ServiceBase
    {
        private const string ServiceNameArgPrefix = "--servicename=";
        private const string EtwArea = nameof(GVFSService);

        private JsonTracer tracer;
        private Thread serviceThread;
        private ManualResetEvent serviceStopped;
        private string serviceName;
        private string serviceDataLocation;
        private RepoRegistry repoRegistry;
        private WindowsRequestHandler requestHandler;
        private INotificationHandler notificationHandler;
        private PendingUpgradeMonitor pendingUpgradeMonitor;
        private DeferredTelemetryAttacher telemetryAttacher;

        public GVFSService(JsonTracer tracer)
        {
            this.tracer = tracer;
            this.serviceName = GVFSConstants.Service.ServiceName;
            this.CanHandleSessionChangeEvent = true;
            this.notificationHandler = new NotificationHandler(tracer);
        }

        public void Run()
        {
            try
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Version", ProcessHelper.GetCurrentProcessVersion());
                this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(GVFSService)}_{nameof(this.Run)}", metadata);

                // Set up deferred telemetry pipe attachment FIRST, before any
                // telemetry-emitting work (particularly PendingUpgradeHandler,
                // whose Deferred/Complete events we want to capture).
                //
                // The service runs as SYSTEM and can't read the user's global
                // git config (where gvfs.telemetry-pipe is configured) at
                // startup.  The DeferredTelemetryAttacher adds a
                // BufferingTelemetryListener that captures events in memory,
                // then replays them once the real pipe listener attaches.
                //
                // Three attach paths exist:
                //   1. TryAttachTelemetryPipeForAnySessions() below — tries
                //      all Active/Disconnected sessions immediately.
                //   2. OnSessionChange (SessionLogon) — fires when a new user
                //      logs in after the service is already running.
                //   3. StartRetryTimer — periodic retry (10s, 30s, 1m, 5m)
                //      as a fallback, reads system config only (no user
                //      config available without a session to impersonate).
                string gitBinRoot = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
                if (!string.IsNullOrEmpty(gitBinRoot))
                {
                    this.telemetryAttacher = new DeferredTelemetryAttacher(
                        this.tracer,
                        GVFSConstants.Service.ServiceName,
                        enlistmentId: null,
                        mountId: null);
                    this.telemetryAttacher.StartRetryTimer(gitBinRoot);

                    // If a user is already logged in (e.g. service restart
                    // during active session), try attaching immediately by
                    // enumerating all interactive sessions.  This is needed
                    // because WTSGetActiveConsoleSessionId only returns the
                    // physical console session, which on Cloud PCs / DevBoxes
                    // (RDP-only) has no logged-in user.  The actual user is
                    // in an RDP session that the console-only check misses.
                    // SessionLogon events also won't fire for sessions that
                    // were already established before the service started.
                    this.TryAttachTelemetryPipeForAnySessions();
                }

                // Check for a staged upgrade before doing anything else.
                // If no GVFS.Mount processes are running (typical at boot or after
                // unmount-all), copy staged files in-place and proceed normally.
                // If mounts ARE running, start a monitor that will apply the
                // upgrade when all mount processes exit.
                UpgradeResult upgradeResult = PendingUpgradeHandler.TryApplyPendingUpgrade(this.tracer);
                if (upgradeResult == UpgradeResult.DeferredMountsRunning)
                {
                    this.pendingUpgradeMonitor = new PendingUpgradeMonitor(this.tracer);
                    this.pendingUpgradeMonitor.Start();
                }

                this.repoRegistry = new RepoRegistry(
                    this.tracer,
                    new PhysicalFileSystem(),
                    Path.Combine(GVFSPlatform.Instance.GetCommonAppDataRootForGVFS(), this.serviceName),
                    new GVFSMountProcess(this.tracer),
                    this.notificationHandler);
                this.repoRegistry.Upgrade();
                this.requestHandler = new WindowsRequestHandler(this.tracer, EtwArea, this.repoRegistry);

                string pipeName = GVFSPlatform.Instance.GetGVFSServiceNamedPipeName(this.serviceName);
                this.tracer.RelatedInfo("Starting pipe server with name: " + pipeName);

                using (NamedPipeServer pipeServer = NamedPipeServer.StartNewServer(
                    pipeName,
                    this.tracer,
                    this.requestHandler.HandleRequest))
                {
                    this.CheckEnableGitStatusCacheTokenFile();

                    using (ITracer activity = this.tracer.StartActivity("EnsurePrjFltHealthy", EventLevel.Informational))
                    {
                        // Make a best-effort to enable PrjFlt. Continue even if it fails.
                        // This will be tried again when user attempts to mount an enlistment.
                        string error;
                        EnableAndAttachProjFSHandler.TryEnablePrjFlt(activity, out error);
                    }

                    this.serviceStopped.WaitOne();
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.Run));
            }
        }

        public void StopRunning()
        {
            if (this.serviceStopped == null)
            {
                return;
            }

            try
            {
                if (this.tracer != null)
                {
                    this.tracer.RelatedInfo("Stopping");
                }

                if (this.telemetryAttacher != null)
                {
                    this.telemetryAttacher.Dispose();
                    this.telemetryAttacher = null;
                }

                if (this.pendingUpgradeMonitor != null)
                {
                    this.pendingUpgradeMonitor.Dispose();
                    this.pendingUpgradeMonitor = null;
                }

                if (this.serviceStopped != null)
                {
                    this.serviceStopped.Set();
                }

                if (this.serviceThread != null)
                {
                    this.serviceThread.Join();
                    this.serviceThread = null;

                    if (this.serviceStopped != null)
                    {
                        this.serviceStopped.Dispose();
                        this.serviceStopped = null;
                    }
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.StopRunning));
            }
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            try
            {
                base.OnSessionChange(changeDescription);

                if (!GVFSEnlistment.IsUnattended(tracer: null))
                {
                    if (changeDescription.Reason == SessionChangeReason.SessionLogon)
                    {
                        this.tracer.RelatedInfo("SessionLogon detected, sessionId: {0}", changeDescription.SessionId);

                        // Attempt to attach the telemetry pipe now that a user
                        // session is available.  Buffered pre-logon events are
                        // replayed.  No-ops if already attached.
                        this.TryAttachTelemetryPipeForSession(changeDescription.SessionId);

                        using (ITracer activity = this.tracer.StartActivity("LogonAutomount", EventLevel.Informational))
                        {
                            this.repoRegistry.AutoMountRepos(
                                GVFSPlatform.Instance.GetUserIdFromLoginSessionId(changeDescription.SessionId, this.tracer),
                                changeDescription.SessionId);
                            this.repoRegistry.TraceStatus();
                        }
                    }
                    else if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
                    {
                        this.tracer.RelatedInfo("SessionLogoff detected");
                    }
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnSessionChange));
            }
        }

        public void RunInConsoleMode(string[] args)
        {
            if (this.serviceThread != null)
            {
                throw new InvalidOperationException("Cannot start service twice in a row.");
            }

            string serviceName = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix));
            if (serviceName != null)
            {
                this.serviceName = serviceName.Substring(ServiceNameArgPrefix.Length);
            }

            string serviceLogsDirectoryPath = GVFSPlatform.Instance.GetLogsDirectoryForGVFSComponent(this.serviceName);

            Directory.CreateDirectory(serviceLogsDirectoryPath);
            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(serviceLogsDirectoryPath, GVFSConstants.LogFileTypes.Service),
                EventLevel.Verbose,
                Keywords.Any);

            try
            {
                this.serviceDataLocation = GVFSPlatform.Instance.GetSecureDataRootForGVFSComponent(this.serviceName);
                Directory.CreateDirectory(this.serviceDataLocation);
                Directory.CreateDirectory(Path.GetDirectoryName(this.serviceDataLocation));

                this.serviceStopped = new ManualResetEvent(false);

                Console.WriteLine($"GVFS.Service running in console mode as '{this.serviceName}'");
                Console.WriteLine("Press Ctrl+C to stop.");

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    this.StopRunning();
                };

                this.Run();
            }
            catch (Exception e)
            {
                this.tracer.RelatedError($"Console mode failed: {e}");
                throw;
            }
        }

        protected override void OnStart(string[] args)
        {
            if (this.serviceThread != null)
            {
                throw new InvalidOperationException("Cannot start service twice in a row.");
            }

            // TODO: 865304 Used for functional tests and development only. Replace with a smarter appConfig-based solution
            string serviceName = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix));
            if (serviceName != null)
            {
                this.serviceName = serviceName.Substring(ServiceNameArgPrefix.Length);
            }

            string serviceLogsDirectoryPath = GVFSPlatform.Instance.GetLogsDirectoryForGVFSComponent(this.serviceName);

            // Create the logs directory explicitly *before* creating a log file event listener to ensure that it
            // and its ancestor directories are created with the correct ACLs.
            this.CreateServiceLogsDirectory(serviceLogsDirectoryPath);
            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(serviceLogsDirectoryPath, GVFSConstants.LogFileTypes.Service),
                EventLevel.Verbose,
                Keywords.Any);

            try
            {
                this.serviceDataLocation = GVFSPlatform.Instance.GetSecureDataRootForGVFSComponent(this.serviceName);
                this.CreateAndConfigureProgramDataDirectories();
                this.Start();
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnStart));
            }
        }

        protected override void OnStop()
        {
            try
            {
                this.StopRunning();
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnStart));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.StopRunning();

                if (this.tracer != null)
                {
                    this.tracer.Dispose();
                    this.tracer = null;
                }
            }

            base.Dispose(disposing);
        }

        private void Start()
        {
            if (this.serviceStopped != null)
            {
                return;
            }

            this.serviceStopped = new ManualResetEvent(false);
            this.serviceThread = new Thread(this.Run);

            this.serviceThread.Start();
        }

        /// <summary>
        /// To work around a behavior in ProjFS where notification masks on files that have been opened in virtualization instance are not invalidated
        /// when the virtualization instance is restarted, GVFS waits until after there has been a reboot before enabling the GitStatusCache.
        /// GVFS.Service signals that there has been a reboot since installing a version of GVFS that supports the GitStatusCache via
        /// the existence of the file "EnableGitStatusCacheToken.dat" in {CommonApplicationData}\GVFS\GVFS.Service
        /// (i.e. ProgramData\GVFS\GVFS.Service\EnableGitStatusCacheToken.dat on Windows).
        /// </summary>
        private void CheckEnableGitStatusCacheTokenFile()
        {
            try
            {
                string statusCacheVersionTokenPath = Path.Combine(GVFSPlatform.Instance.GetSecureDataRootForGVFSComponent(GVFSConstants.Service.ServiceName), GVFSConstants.GitStatusCache.EnableGitStatusCacheTokenFile);
                if (File.Exists(statusCacheVersionTokenPath))
                {
                    this.tracer.RelatedInfo($"CheckEnableGitStatusCache: EnableGitStatusCacheToken file already exists at {statusCacheVersionTokenPath}.");
                    return;
                }

                DateTime lastRebootTime = NativeMethods.GetLastRebootTime();

                // GitStatusCache was included with GVFS on disk version 16. The 1st time GVFS that is at or above on disk version
                // is installed, it will write out a file indicating that the installation is "OnDiskVersion16Capable".
                // We can query the properties of this file to get the installation time, and compare this with the last reboot time for
                // this machine.
                string fileToCheck = Path.Combine(Configuration.AssemblyPath, GVFSConstants.InstallationCapabilityFiles.OnDiskVersion16CapableInstallation);

                if (File.Exists(fileToCheck))
                {
                    DateTime installTime = File.GetCreationTime(fileToCheck);
                    if (lastRebootTime > installTime)
                    {
                        this.tracer.RelatedInfo($"CheckEnableGitStatusCache: Writing out EnableGitStatusCacheToken file. GVFS installation time: {installTime}, last Reboot time: {lastRebootTime}.");
                        File.WriteAllText(statusCacheVersionTokenPath, string.Empty);
                    }
                    else
                    {
                        this.tracer.RelatedInfo($"CheckEnableGitStatusCache: Not writing EnableGitStatusCacheToken file - machine has not been rebooted since OnDiskVersion16Capable installation. GVFS installation time: {installTime}, last reboot time: {lastRebootTime}");
                    }
                }
                else
                {
                    this.tracer.RelatedError($"Unable to determine GVFS installation time: {fileToCheck} does not exist.");
                }
            }
            catch (Exception ex)
            {
                // Do not crash the service if there is an error here. Service is still healthy, but we
                // might not create file indicating that it is OK to use GitStatusCache.
                this.tracer.RelatedError($"{nameof(this.CheckEnableGitStatusCacheTokenFile)}: Unable to determine GVFS installation time or write EnableGitStatusCacheToken file due to exception. Exception: {ex.ToString()}");
            }
        }

        private void LogExceptionAndExit(Exception e, string method)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("Exception", e.ToString());
            this.tracer.RelatedError(metadata, "Unhandled exception in " + method);
            Environment.Exit((int)ReturnCode.GenericError);
        }

        private void CreateServiceLogsDirectory(string serviceLogsDirectoryPath)
        {
            if (!Directory.Exists(serviceLogsDirectoryPath))
            {
                DirectorySecurity serviceDataRootSecurity = this.GetServiceDirectorySecurity(serviceLogsDirectoryPath);
                Directory.CreateDirectory(serviceLogsDirectoryPath);
            }
        }

        private void CreateAndConfigureProgramDataDirectories()
        {
            string serviceDataRootPath = Path.GetDirectoryName(this.serviceDataLocation);

            DirectorySecurity serviceDataRootSecurity = this.GetServiceDirectorySecurity(serviceDataRootPath);

            // Create GVFS.Service related directories (if they don't already exist)
            serviceDataRootSecurity.CreateDirectory(serviceDataRootPath);
            serviceDataRootSecurity.CreateDirectory(this.serviceDataLocation);

            // Ensure the ACLs are set correctly on any files or directories that were already created (e.g. after upgrading VFS4G)
            new DirectoryInfo(serviceDataRootPath).SetAccessControl(serviceDataRootSecurity);
        }

        private void CreateAndConfigureLogDirectory(string path)
        {
            string error;
            if (!GVFSPlatform.Instance.FileSystem.TryCreateDirectoryWithAdminAndUserModifyPermissions(path, out error))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", EtwArea);
                metadata.Add(nameof(path), path);
                metadata.Add(nameof(error), error);
                this.tracer.RelatedWarning(
                    metadata,
                    $"{nameof(this.CreateAndConfigureLogDirectory)}: Failed to create logs directory",
                    Keywords.Telemetry);
            }
        }

        /// <summary>
        /// Resolves the logged-on user's profile directory and passes their
        /// .gitconfig path directly to the deferred attacher, which reads it
        /// with <c>git config --file</c>.  This avoids mutating the
        /// process-wide HOME environment variable, which would leak into
        /// any concurrent git operations in the service.
        /// </summary>
        private void TryAttachTelemetryPipeForSession(int sessionId)
        {
            if (this.telemetryAttacher == null || this.telemetryAttacher.IsAttached)
            {
                return;
            }

            try
            {
                using (CurrentUser user = new CurrentUser(this.tracer, sessionId))
                {
                    if (user.Identity == null)
                    {
                        this.tracer.RelatedWarning("TryAttachTelemetryPipe: Could not get user identity for session {0}", sessionId);
                        return;
                    }

                    string gitBinRoot = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

                    string userProfile = null;
                    WindowsIdentity.RunImpersonated(user.Identity.AccessToken, () =>
                    {
                        userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    });

                    if (string.IsNullOrEmpty(userProfile))
                    {
                        this.tracer.RelatedWarning("TryAttachTelemetryPipe: Could not resolve user profile for session {0}", sessionId);
                        return;
                    }

                    string globalConfigPath = Path.Combine(userProfile, ".gitconfig");
                    this.telemetryAttacher.TryAttach(gitBinRoot, globalConfigPath);
                }
            }
            catch (Exception e)
            {
                this.tracer.RelatedWarning("TryAttachTelemetryPipe failed: {0}", e.Message);
            }
        }

        /// <summary>
        /// Enumerates all interactive sessions (Active and Disconnected)
        /// and tries to attach the telemetry pipe using each session's
        /// user profile.  Stops on the first successful attach.
        /// </summary>
        /// <remarks>
        /// This method exists because the console-only check
        /// (<c>WTSGetActiveConsoleSessionId</c>) fails on Cloud PCs and
        /// RDP-only machines where the console session is in the Connected
        /// state (login screen, no user).  Disconnected sessions are also
        /// checked because an RDP user who disconnected without logging
        /// off still has a valid token and git config.
        /// </remarks>
        private void TryAttachTelemetryPipeForAnySessions()
        {
            if (this.telemetryAttacher == null || this.telemetryAttacher.IsAttached)
            {
                return;
            }

            List<int> sessionIds = CurrentUser.GetInteractiveSessionIds(this.tracer);
            if (sessionIds.Count == 0)
            {
                this.tracer.RelatedInfo("TryAttachTelemetryPipeForAnySessions: No interactive sessions found");
                return;
            }

            foreach (int sessionId in sessionIds)
            {
                this.TryAttachTelemetryPipeForSession(sessionId);
                if (this.telemetryAttacher.IsAttached)
                {
                    break;
                }
            }

            if (!this.telemetryAttacher.IsAttached)
            {
                this.tracer.RelatedWarning(
                    "TryAttachTelemetryPipeForAnySessions: Could not attach from any of {0} interactive session(s)",
                    sessionIds.Count);
            }
        }

        private DirectorySecurity GetServiceDirectorySecurity(string serviceDataRootPath)
        {
            DirectorySecurity serviceDataRootSecurity;
            if (Directory.Exists(serviceDataRootPath))
            {
                this.tracer.RelatedInfo($"{nameof(this.GetServiceDirectorySecurity)}: {serviceDataRootPath} exists, modifying ACLs.");
                serviceDataRootSecurity = new DirectoryInfo(serviceDataRootPath).GetAccessControl();
            }
            else
            {
                this.tracer.RelatedInfo($"{nameof(this.GetServiceDirectorySecurity)}: {serviceDataRootPath} does not exist, creating new ACLs.");
                serviceDataRootSecurity = new DirectorySecurity();
            }

            // Protect the access rules from inheritance and remove any inherited rules
            serviceDataRootSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Remove any existing ACLs and add new ACLs for users and admins
            WindowsFileSystem.RemoveAllFileSystemAccessRulesFromDirectorySecurity(serviceDataRootSecurity);
            WindowsFileSystem.AddUsersAccessRulesToDirectorySecurity(serviceDataRootSecurity, grantUsersModifyPermissions: false);
            WindowsFileSystem.AddAdminAccessRulesToDirectorySecurity(serviceDataRootSecurity);

            return serviceDataRootSecurity;
        }

    }
}
