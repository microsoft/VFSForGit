using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace GVFS.Service.Handlers
{
    public class RunInstallerHandler
    {
        private const string InstallerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /STAGEIFMOUNTED=true /LOG=\"{0}\"";

        private readonly ITracer tracer;
        private readonly NamedPipeServer.Connection connection;
        private readonly NamedPipeMessages.RunInstallerRequest request;

        public RunInstallerHandler(
            ITracer tracer,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.RunInstallerRequest request)
        {
            this.tracer = tracer;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            NamedPipeMessages.RunInstallerRequest.Response response =
                new NamedPipeMessages.RunInstallerRequest.Response();

            try
            {
                string installerPath = this.request.InstallerPath;
                if (string.IsNullOrWhiteSpace(installerPath))
                {
                    response.State = NamedPipeMessages.CompletionState.Failure;
                    response.ErrorMessage = "Installer path is required";
                    this.tracer.RelatedError(this.CreateBaseMetadata(), response.ErrorMessage);
                    return;
                }

                // Resolve to full path to prevent path traversal.
                installerPath = Path.GetFullPath(installerPath);

                // --allow-unsigned is a debug-build-only escape hatch and
                // is rejected outright in release builds. Defense-in-depth
                // against a malicious client crafting a raw pipe request
                // bypassing the CLI's debug-only flag registration.
#if !DEBUG
                if (this.request.AllowUnsigned)
                {
                    response.State = NamedPipeMessages.CompletionState.Failure;
                    response.ErrorMessage =
                        "--allow-unsigned is not supported in release builds. " +
                        "Use a signed installer.";
                    this.tracer.RelatedError(this.CreateBaseMetadata(), response.ErrorMessage);
                    return;
                }
#else
                // Debug builds still require the caller to be an
                // Administrator when using --allow-unsigned: an unsigned
                // installer is trivially attacker-controlled (any user can
                // stamp ProductName="VFS for Git" onto a binary), and the
                // pipe is open to BUILTIN\Users. Without this check, any
                // non-admin user on a debug-build dev machine could get
                // LocalSystem code execution through the service.
                if (this.request.AllowUnsigned && !this.IsCallerAdministrator(out string identityError))
                {
                    response.State = NamedPipeMessages.CompletionState.Failure;
                    response.ErrorMessage =
                        "--allow-unsigned upgrades require Administrator privileges. " +
                        "Either use a signed installer or run 'gvfs upgrade --allow-unsigned' " +
                        "from an elevated shell.";
                    if (!string.IsNullOrEmpty(identityError))
                    {
                        response.ErrorMessage += $" ({identityError})";
                    }
                    this.tracer.RelatedError(this.CreateBaseMetadata(), response.ErrorMessage);
                    return;
                }
#endif

                // Hold a deny-write/delete handle on the installer for the
                // lifetime of verify+launch to prevent a TOCTOU swap by a
                // non-admin caller (FileShare.Read allows readers but
                // blocks any write, delete, or rename of the path).
                FileStream installerLock;
                try
                {
                    installerLock = new FileStream(
                        installerPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                }
                catch (Exception ex) when (ex is FileNotFoundException
                                          || ex is DirectoryNotFoundException
                                          || ex is UnauthorizedAccessException
                                          || ex is IOException)
                {
                    response.State = NamedPipeMessages.CompletionState.Failure;
                    response.ErrorMessage = $"Failed to open installer for verification: {ex.Message}";
                    this.tracer.RelatedError(this.CreateBaseMetadata(), response.ErrorMessage);
                    return;
                }

                using (installerLock)
                {
                    if (!InstallerVerifier.TryVerifyInstaller(
                            this.tracer,
                            installerPath,
                            this.request.AllowUnsigned,
                            out string verifyError))
                    {
                        response.State = NamedPipeMessages.CompletionState.Failure;
                        response.ErrorMessage = verifyError;
                        return;
                    }

                    string logPath = Path.Combine(
                        Configuration.AssemblyPath,
                        "ProgramData",
                        "upgrade-install.log");

                    this.tracer.RelatedInfo(
                        this.CreateBaseMetadata(),
                        $"{nameof(RunInstallerHandler)}: Verification passed, launching installer (log: {logPath})");

                    int exitCode = LaunchInstallerAndWait(installerPath, logPath);

                    EventMetadata launchMetadata = this.CreateBaseMetadata();
                    launchMetadata.Add("InstallerExitCode", exitCode);

                    if (exitCode == 0)
                    {
                        response.State = NamedPipeMessages.CompletionState.Success;
                        this.tracer.RelatedInfo(
                            launchMetadata,
                            $"{nameof(RunInstallerHandler)}: Installer launched successfully");
                    }
                    else
                    {
                        response.State = NamedPipeMessages.CompletionState.Failure;
                        response.ErrorMessage = $"Installer failed to launch (error {exitCode})";
                        this.tracer.RelatedError(launchMetadata, response.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                response.State = NamedPipeMessages.CompletionState.Failure;
                response.ErrorMessage = $"Failed to run installer: {ex.Message}";
                EventMetadata exceptionMetadata = this.CreateBaseMetadata();
                exceptionMetadata.Add("Exception", ex.ToString());
                this.tracer.RelatedError(
                    exceptionMetadata,
                    $"{nameof(RunInstallerHandler)}: {response.ErrorMessage}");
            }
            finally
            {
                this.connection.TrySendResponse(response.ToMessage().ToString());
            }
        }

        /// <summary>
        /// Returns a fresh <see cref="EventMetadata"/> populated with the
        /// request's identifying fields. A new instance must be used for each
        /// tracer call because <see cref="JsonTracer"/> mutates the metadata
        /// (adds the "Message" key); reusing the same instance across calls
        /// triggers a duplicate-key exception.
        /// </summary>
        private EventMetadata CreateBaseMetadata()
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("InstallerPath", this.request?.InstallerPath);
            metadata.Add("AllowUnsigned", this.request?.AllowUnsigned ?? false);
            return metadata;
        }

        /// <summary>
        /// Impersonates the pipe client and tests whether the caller is in
        /// the local Administrators group. Returns false (and sets
        /// <paramref name="error"/>) if impersonation fails or the caller is
        /// a regular user. Required because the service runs as LocalSystem
        /// and the pipe is accessible to all interactive users — without
        /// this check, any non-admin user could request privileged
        /// operations.
        /// </summary>
        private bool IsCallerAdministrator(out string error)
        {
            // Named-pipe client impersonation is a Windows-only capability.
            // RunInstallerHandler runs in GVFS.Service which is Windows-only
            // in practice, but guard anyway so we fail closed.
            if (!OperatingSystem.IsWindows())
            {
                error = "Client identity check is not supported on this platform";
                return false;
            }

            bool isAdmin = false;
            string innerError = null;

            bool impersonated = this.connection.TryRunAsClient(() =>
            {
                try
                {
                    isAdmin = IsCurrentUserAdministrator();
                }
                catch (Exception ex)
                {
                    innerError = ex.Message;
                }
            });

            if (!impersonated)
            {
                error = "Failed to impersonate pipe client";
                return false;
            }

            if (innerError != null)
            {
                error = innerError;
                return false;
            }

            error = null;
            return isAdmin;
        }

        [SupportedOSPlatform("windows")]
        private static bool IsCurrentUserAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Launches the installer as a detached process. The installer will
        /// stop GVFS.Service as part of its upgrade flow, so we must not wait
        /// for it to exit (that would deadlock — parent waiting on child that
        /// kills parent). Returns 0 if the process started, or -1 on failure.
        /// </summary>
        private static int LaunchInstallerAndWait(string installerPath, string logPath)
        {
            string args = string.Format(InstallerArgs, logPath);
            try
            {
                Process installerProcess = new Process();
                installerProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (!installerProcess.Start())
                {
                    return -1;
                }

                // Do NOT call WaitForExit(). The installer will stop
                // GVFS.Service (our parent), so we'd deadlock.
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
