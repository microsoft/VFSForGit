using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
        private ProductUpgradeTimer productUpgradeTimer;

        public GVFSService(JsonTracer tracer)
        {
            this.tracer = tracer;
            this.serviceName = GVFSConstants.Service.ServiceName;
            this.CanHandleSessionChangeEvent = true;
            this.productUpgradeTimer = new ProductUpgradeTimer(tracer);
        }

        public void Run()
        {
            try
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Version", ProcessHelper.GetCurrentProcessVersion());
                this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(GVFSService)}_{nameof(this.Run)}", metadata);

                this.repoRegistry = new RepoRegistry(this.tracer, new PhysicalFileSystem(), this.serviceDataLocation);
                this.repoRegistry.Upgrade();
                this.productUpgradeTimer.Start();
                string pipeName = this.serviceName + ".Pipe";
                this.tracer.RelatedInfo("Starting pipe server with name: " + pipeName);

                using (NamedPipeServer pipeServer = NamedPipeServer.StartNewServer(pipeName, this.tracer, this.HandleRequest))
                {
                    this.CheckEnableGitStatusCacheTokenFile();

                    using (ITracer activity = this.tracer.StartActivity("EnsurePrjFltHealthy", EventLevel.Informational))
                    {
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
                if (this.productUpgradeTimer != null)
                {
                    this.productUpgradeTimer.Stop();
                }

                if (this.tracer != null)
                {
                    this.tracer.RelatedInfo("Stopping");
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
                        using (ITracer activity = this.tracer.StartActivity("LogonAutomount", EventLevel.Informational))
                        {
                            this.repoRegistry.AutoMountRepos(changeDescription.SessionId);
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

            this.CreateAndConfigureProgramDataDirectories();

            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(Paths.GetServiceLogsPath(this.serviceName), GVFSConstants.LogFileTypes.Service),
                EventLevel.Verbose,
                Keywords.Any);

            try
            {
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
            this.StopRunning();

            if (this.tracer != null)
            {
                this.tracer.Dispose();
                this.tracer = null;
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

        private void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);
            if (string.IsNullOrWhiteSpace(message.Header))
            {
                return;
            }

            using (ITracer activity = this.tracer.StartActivity(message.Header, EventLevel.Informational, new EventMetadata { { "request", request } }))
            {
                switch (message.Header)
                {
                    case NamedPipeMessages.RegisterRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.RegisterRepoRequest mountRequest = NamedPipeMessages.RegisterRepoRequest.FromMessage(message);
                            RegisterRepoHandler mountHandler = new RegisterRepoHandler(activity, this.repoRegistry, connection, mountRequest);
                            mountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize mount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.UnregisterRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.UnregisterRepoRequest unmountRequest = NamedPipeMessages.UnregisterRepoRequest.FromMessage(message);
                            UnregisterRepoHandler unmountHandler = new UnregisterRepoHandler(activity, this.repoRegistry, connection, unmountRequest);
                            unmountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize unmount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.EnableAndAttachProjFSRequest.Header:
                        try
                        {
                            NamedPipeMessages.EnableAndAttachProjFSRequest attachRequest = NamedPipeMessages.EnableAndAttachProjFSRequest.FromMessage(message);
                            EnableAndAttachProjFSHandler attachHandler = new EnableAndAttachProjFSHandler(activity, connection, attachRequest);
                            attachHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize attach volume request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.GetActiveRepoListRequest.Header:
                        try
                        {
                            NamedPipeMessages.GetActiveRepoListRequest repoListRequest = NamedPipeMessages.GetActiveRepoListRequest.FromMessage(message);
                            GetActiveRepoListHandler excludeHandler = new GetActiveRepoListHandler(activity, this.repoRegistry, connection, repoListRequest);
                            excludeHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize repo list request: {0}", ex.Message);
                        }

                        break;

                    default:
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add("Header", message.Header);
                        this.tracer.RelatedWarning(metadata, "HandleNewConnection: Unknown request", Keywords.Telemetry);

                        connection.TrySendResponse(NamedPipeMessages.UnknownRequest);
                        break;
                }
            }
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
                string statusCacheVersionTokenPath = Path.Combine(Paths.GetServiceDataRoot(GVFSConstants.Service.ServiceName), GVFSConstants.GitStatusCache.EnableGitStatusCacheTokenFile);
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

        private void CreateAndConfigureProgramDataDirectories()
        {
            this.serviceDataLocation = Paths.GetServiceDataRoot(this.serviceName);
            string serviceDataRootPath = Path.GetDirectoryName(this.serviceDataLocation);

            DirectorySecurity serviceDataRootSecurity;
            if (Directory.Exists(serviceDataRootPath))
            {
                serviceDataRootSecurity = Directory.GetAccessControl(serviceDataRootPath);
            }
            else
            {
                serviceDataRootSecurity = new DirectorySecurity();
            }

            // Protect the access rules from inheritance and remove any inherited rules
            serviceDataRootSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Remove any existing ACLs and add new ACLs for users and admins
            WindowsFileSystem.RemoveAllFileSystemAccessRulesFromDirectorySecurity(serviceDataRootSecurity);
            WindowsFileSystem.AddUsersAccessRulesToDirectorySecurity(serviceDataRootSecurity, grantUsersModifyPermissions: false);
            WindowsFileSystem.AddAdminAccessRulesToDirectorySecurity(serviceDataRootSecurity);

            Directory.CreateDirectory(serviceDataRootPath, serviceDataRootSecurity);
            Directory.CreateDirectory(this.serviceDataLocation, serviceDataRootSecurity);
            Directory.CreateDirectory(ProductUpgraderInfo.GetUpgradesDirectoryPath(), serviceDataRootSecurity);

            // Ensure the ACLs are set correctly on any files or directories that were already created (e.g. after upgrading VFS4G)
            Directory.SetAccessControl(serviceDataRootPath, serviceDataRootSecurity);

            this.CreateAndConfigureUpgradeLogDirectory();
        }

        private void CreateAndConfigureUpgradeLogDirectory()
        {
            // Special rules for the upgrader logs, as non-elevated users need to be be able to write
            string upgradeLogsPath = ProductUpgraderInfo.GetLogDirectoryPath();
            DirectorySecurity upgradeLogsSecurity;
            if (Directory.Exists(upgradeLogsPath))
            {
                upgradeLogsSecurity = Directory.GetAccessControl(upgradeLogsPath);
            }
            else
            {
                upgradeLogsSecurity = new DirectorySecurity();
            }

            // Protect the access rules from inheritance and remove any inherited rules
            // (any manually added ACLs are left in place)
            upgradeLogsSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Add new ACLs for users and admins
            WindowsFileSystem.AddUsersAccessRulesToDirectorySecurity(upgradeLogsSecurity, grantUsersModifyPermissions: true);
            WindowsFileSystem.AddAdminAccessRulesToDirectorySecurity(upgradeLogsSecurity);

            Directory.CreateDirectory(upgradeLogsPath, upgradeLogsSecurity);

            // Ensure the ACLs are set correct on any files or directories that were already created (e.g. after upgrading VFS4G)
            Directory.SetAccessControl(upgradeLogsPath, upgradeLogsSecurity);
        }
    }
}
