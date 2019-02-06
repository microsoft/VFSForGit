using GVFS.Common.FileSystem;
using GVFS.Common.NuGetUpgrade;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    /// <summary>
    /// Delegate to wrap install action steps in.
    /// This can be used to report the beginning / end of each install step.
    /// </summary>
    /// <param name="method">The method to run inside wrapper</param>
    /// <param name="message">The message to display</param>
    /// <returns>success or failure return from the method run.</returns>
    public delegate bool InstallActionWrapper(Func<bool> method, string message);

    public abstract class ProductUpgrader : IDisposable
    {
        protected readonly Version installedVersion;
        protected readonly ITracer tracer;
        protected readonly PhysicalFileSystem fileSystem;

        protected bool noVerify;
        protected bool dryRun;

        private const string ToolsDirectory = "Tools";
        private static readonly string UpgraderToolName = GVFSPlatform.Instance.Constants.GVFSUpgraderExecutableName;
        private static readonly string UpgraderToolConfigFile = UpgraderToolName + ".config";
        private static readonly string[] UpgraderToolAndLibs =
            {
                UpgraderToolName,
                UpgraderToolConfigFile,
                "GVFS.Common.dll",
                "GVFS.Platform.Windows.dll",
                "Microsoft.Diagnostics.Tracing.EventSource.dll",
                "netstandard.dll",
                "System.Net.Http.dll",
                "Newtonsoft.Json.dll",
                "CommandLine.dll",
                "NuGet.Common.dll",
                "NuGet.Configuration.dll",
                "NuGet.Frameworks.dll",
                "NuGet.Packaging.Core.dll",
                "NuGet.Packaging.dll",
                "NuGet.Protocol.dll",
                "NuGet.Versioning.dll",
                "System.IO.Compression.dll"
            };

        public ProductUpgrader(
            string currentVersion,
            ITracer tracer,
            bool dryRun,
            bool noVerify,
            PhysicalFileSystem fileSystem)
        {
            this.installedVersion = new Version(currentVersion);
            this.dryRun = dryRun;
            this.noVerify = noVerify;
            this.tracer = tracer;
            this.fileSystem = fileSystem;
        }

        /// <summary>
        /// For mocking purposes only
        /// </summary>
        protected ProductUpgrader()
        {
        }

        public static bool TryCreateUpgrader(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            bool dryRun,
            bool noVerify,
            out ProductUpgrader newUpgrader,
            out string error)
        {
            // Prefer to use the NuGet upgrader if it is configured. If the NuGet upgrader is not configured,
            // then try to use the GitHubUpgrader.
            if (NuGetUpgrader.TryCreate(tracer, fileSystem, dryRun, noVerify, out NuGetUpgrader nuGetUpgrader, out bool isConfigured, out error))
            {
                // We were successfully able to load a NuGetUpgrader - use that.
                newUpgrader = nuGetUpgrader;
                return true;
            }
            else
            {
                if (isConfigured)
                {
                    tracer.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");

                    // We did not successfully load a NuGetUpgrader, but it is configured.
                    newUpgrader = null;
                    return false;
                }

                // We did not load a NuGetUpgrader, but it is not the configured upgrader.
                // Try to load other upgraders as appropriate.
            }

            newUpgrader = GitHubUpgrader.Create(tracer, fileSystem, dryRun, noVerify, out error);
            if (newUpgrader == null)
            {
                tracer.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Deletes any previously downloaded installers in the Upgrader Download directory.
        /// This can include old installers which were downloaded but never installed.
        /// </summary>
        public virtual void DeleteAllInstallerDownloads()
        {
            try
            {
                this.fileSystem.RecursiveDelete(ProductUpgraderInfo.GetAssetDownloadsPath());
            }
            catch (Exception ex)
            {
                this.tracer.RelatedError(
                    $"{nameof(this.DeleteAllInstallerDownloads)}: Could not remove directory: {ProductUpgraderInfo.GetAssetDownloadsPath()}.{ex.ToString()}");
            }
        }

        public abstract bool UpgradeAllowed(out string message);

        public abstract bool TryQueryNewestVersion(out Version newVersion, out string message);

        public abstract bool TryDownloadNewestVersion(out string errorMessage);

        public abstract bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error);

        public virtual bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            string rootDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);

            if (!GVFSPlatform.Instance.FileSystem.TryCreateDirectoryWithAdminOnlyModify(
                    this.tracer,
                    toolsDirectoryPath,
                    out error))
            {
                upgraderToolPath = null;
                return false;
            }

            string currentPath = ProcessHelper.GetCurrentProcessLocation();
            error = null;
            foreach (string name in UpgraderToolAndLibs)
            {
                string toolPath = Path.Combine(currentPath, name);
                string destinationPath = Path.Combine(toolsDirectoryPath, name);
                try
                {
                    this.fileSystem.CopyFile(toolPath, destinationPath, overwrite: true);
                }
                catch (UnauthorizedAccessException e)
                {
                    error = string.Join(
                        Environment.NewLine,
                        "File copy error - " + e.Message,
                        $"Make sure you have write permissions to directory {toolsDirectoryPath} and run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} again.");
                    this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                    break;
                }
                catch (IOException e)
                {
                    error = "File copy error - " + e.Message;
                    this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                    break;
                }
            }

            if (string.IsNullOrEmpty(error))
            {
                // There was no error - set upgradeToolPath and return success.
                upgraderToolPath = Path.Combine(toolsDirectoryPath, UpgraderToolName);
                return true;
            }
            else
            {
                // Encountered error - do not set upgrade tool path and return failure.
                upgraderToolPath = null;
                return false;
            }
        }

        public abstract bool TryCleanup(out string error);

        public void TraceException(Exception exception, string method, string message)
        {
            this.TraceException(this.tracer, exception, method, message);
        }

        public void TraceException(ITracer tracer, Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            tracer.RelatedError(metadata, message);
        }

        public virtual void Dispose()
        {
        }

        protected virtual bool TryCreateAndConfigureDownloadDirectory(ITracer tracer, out string error)
        {
            return GVFSPlatform.Instance.FileSystem.TryCreateDirectoryWithAdminOnlyModify(
                tracer,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                out error);
        }

        protected virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }
    }
}
