using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Service.UI
{
    public class GVFSToastRequestHandler
    {
        private const string VFSForGitAutomountStartTitle= "VFS For Git Automount";
        private const string VFSForGitAutomountStartMessageFormat = "Attempting to mount {0} VFS For Git {1}";
        private const string VFSForGitMultipleRepos = "repos";
        private const string VFSForGitSingleRepo = "repo";

        private const string VFSForGitAutomountSuccessTitle = "VFS For Git Automount";
        private const string VFSForGitAutomountSuccessMessageFormat = "The following VFS For Git repo is now mounted: {0}{1}";

        private const string VFSForGitAutomountErrorTitle = "VFS For Git Automount";
        private const string VFSForGitAutomountErrorMessageFormat = "The following VFS For Git repo failed to mount: {0}{1}";
        private const string VFSForGitAutomountButtonTitle = "Retry";

        private const string VFSForGitUpgradeTitleFormat = "New version {0} is available";
        private const string VFSForGitUpgradeMessage = "Upgrade will unmount and remount VFS For Git repos, ensure you are at a stopping point. When ready, click Upgrade button to run upgrade.";
        private const string VFSForGitUpgradeButtonTitle = "Upgrade";

        private const string VFSForGitRemountActionPrefix = "gvfs mount";
        private const string VFSForGitUpgradeActionPrefix = "gvfs upgrade --confirm";

        private readonly ITracer tracer;
        private readonly IToastNotifier toastNotifier;

        public GVFSToastRequestHandler(IToastNotifier toastNotifier, ITracer tracer)
        {
            this.toastNotifier = toastNotifier;
            this.toastNotifier.UserResponseCallback = this.UserResponseCallback;
            this.tracer = tracer;
        }

        public void HandleToastRequest(ITracer tracer, NamedPipeMessages.Notification.Request request)
        {
            string title = null;
            string message = null;
            string buttonTitle = null;
            string args = null;
            string path = null;

            switch (request.Id)
            {
                case NamedPipeMessages.Notification.Request.Identifier.AutomountStart:
                    string reposSuffix = request.EnlistmentCount <= 1 ? VFSForGitSingleRepo : VFSForGitMultipleRepos;
                    title = VFSForGitAutomountStartTitle;
                    message = string.Format(VFSForGitAutomountStartMessageFormat, request.EnlistmentCount, reposSuffix);
                    break;

                case NamedPipeMessages.Notification.Request.Identifier.MountSuccess:
                    if (this.TryValidatePath(request.Enlistment, out path, this.tracer))
                    {
                        title = VFSForGitAutomountSuccessTitle;
                        message = string.Format(VFSForGitAutomountSuccessMessageFormat, Environment.NewLine, path);
                    }

                    break;

                case NamedPipeMessages.Notification.Request.Identifier.MountFailure:
                    if (this.TryValidatePath(request.Enlistment, out path, this.tracer))
                    {
                        title = VFSForGitAutomountErrorTitle;
                        message = string.Format(VFSForGitAutomountErrorMessageFormat, Environment.NewLine, path);
                        buttonTitle = VFSForGitAutomountButtonTitle;
                        args = $"{VFSForGitRemountActionPrefix} {path}";
                    }

                    break;

                case NamedPipeMessages.Notification.Request.Identifier.UpgradeAvailable:
                    title = string.Format(VFSForGitUpgradeTitleFormat, request.NewVersion);
                    message = string.Format(VFSForGitUpgradeMessage);
                    buttonTitle = VFSForGitUpgradeButtonTitle;
                    args = $"{VFSForGitUpgradeActionPrefix}";
                    break;
            }

            if (title != null && message != null)
            {
                this.toastNotifier.Notify(title, message, buttonTitle, args);
            }
        }

        public void UserResponseCallback(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                this.tracer.RelatedError($"{nameof(this.UserResponseCallback)}: Received null arguments in Toaster callback.");
                return;
            }

            using (ITracer activity = this.tracer.StartActivity("GVFSToastCallback", EventLevel.Informational))
            {
                string gvfsCmd = null;
                bool elevate = false;

                if (args.StartsWith(VFSForGitUpgradeActionPrefix))
                {
                    this.tracer.RelatedInfo($"gvfs upgrade action.");
                    gvfsCmd = "gvfs upgrade --confirm";
                    elevate = true;
                }
                else if (args.StartsWith(VFSForGitRemountActionPrefix))
                {
                    string path = args.Substring(VFSForGitRemountActionPrefix.Length, args.Length - VFSForGitRemountActionPrefix.Length);
                    if (this.TryValidatePath(path, out string enlistment, activity))
                    {
                        this.tracer.RelatedInfo($"gvfs mount action {enlistment}.");
                        gvfsCmd = $"gvfs mount \"{enlistment}\"";
                    }
                    else
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add(nameof(args), args);
                        metadata.Add(nameof(path), path);
                        this.tracer.RelatedError(metadata, $"{nameof(this.UserResponseCallback)}- Invalid enlistment path specified in Toaster callback.");
                    }
                }
                else
                {
                    this.tracer.RelatedError($"{nameof(this.UserResponseCallback)}- Unknown action({args}) specified in Toaster callback.");
                }

                if (!string.IsNullOrEmpty(gvfsCmd))
                {
                    this.launchGVFSInCommandPrompt(gvfsCmd, elevate, activity);
                }
            }
        }

        private bool TryValidatePath(string path, out string validatedPath, ITracer tracer)
        {
            try
            {
                validatedPath = Path.GetFullPath(path);
                return true;
            }
            catch (Exception ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", ex.ToString());
                metadata.Add("Path", path);

                tracer.RelatedError(metadata, $"{nameof(this.TryValidatePath)}: {path}. {ex.ToString()}");
            }

            validatedPath = null;
            return false;
        }

        private void launchGVFSInCommandPrompt(string fullGvfsCmd, bool elevate, ITracer tracer)
        {
            const string cmdPath = "CMD.exe";
            ProcessStartInfo processInfo = new ProcessStartInfo(cmdPath);
            processInfo.UseShellExecute = true;
            processInfo.RedirectStandardInput = false;
            processInfo.RedirectStandardOutput = false;
            processInfo.RedirectStandardError = false;
            processInfo.WindowStyle = ProcessWindowStyle.Normal;
            processInfo.CreateNoWindow = false;

            // /K option is so the user gets the time to read the output of the command and
            // manually close the cmd window after that.
            processInfo.Arguments = "/K " + fullGvfsCmd;
            if (elevate)
            {
                processInfo.Verb = "runas";
            }

            tracer.RelatedInfo($"{nameof(this.UserResponseCallback)}- Running {cmdPath} /K {fullGvfsCmd}");

            try
            {
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", ex.ToString());
                metadata.Add(nameof(fullGvfsCmd), fullGvfsCmd);
                metadata.Add(nameof(elevate), elevate);

                tracer.RelatedError(metadata, $"{nameof(this.launchGVFSInCommandPrompt)}: Error launching {fullGvfsCmd}. {ex.ToString()}");
            }
        }
    }
}
