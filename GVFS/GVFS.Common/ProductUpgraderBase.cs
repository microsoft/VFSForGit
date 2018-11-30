using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GVFS.Common
{
    public abstract class ProductUpgraderBase
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";

        protected const string RootDirectory = UpgradeDirectoryName;
        protected const string GVFSInstallerFileNamePrefix = "SetupGVFS";
        protected const string VFSForGitInstallerFileNamePrefix = "VFSForGit";
        protected const string CommonInstallerArgs = "/VERYSILENT /CLOSEAPPLICATIONS /SUPPRESSMSGBOXES /NORESTART";
        protected const string GVFSInstallerArgs = CommonInstallerArgs + " /MOUNTREPOS=false";
        protected const string GitInstallerArgs = CommonInstallerArgs + " /ALLOWDOWNGRADE=1";
        protected const int RepoMountFailureExitCode = 17;
        protected const string ToolsDirectory = "Tools";
        protected static readonly string UpgraderToolName = GVFSPlatform.Instance.Constants.GVFSUpgraderExecutableName;
        protected static readonly string UpgraderToolConfigFile = UpgraderToolName + ".config";
        protected static readonly string[] UpgraderToolAndLibs =
            {
                UpgraderToolName,
                UpgraderToolConfigFile,
                "GVFS.Common.dll",
                "GVFS.Platform.Windows.dll",
                "Microsoft.Diagnostics.Tracing.EventSource.dll",
                "netstandard.dll",
                "System.Net.Http.dll",
                "Newtonsoft.Json.dll"
            };

        protected ProductUpgraderBase(string currentVersion, ITracer tracer)
        {
            this.InstalledVersion = new Version(currentVersion);
            this.Tracer = tracer;
            this.FileSystem = new PhysicalFileSystem();

            string upgradesDirectoryPath = GetUpgradesDirectoryPath();
            this.FileSystem.CreateDirectory(upgradesDirectoryPath);
        }

        protected Version InstalledVersion { get; set; }
        protected PhysicalFileSystem FileSystem { get; set; }
        protected ITracer Tracer { get; set; }

        public static ProductUpgraderBase LoadUpgrader(ITracer tracer)
        {
            LocalGVFSConfig localConfig = new LocalGVFSConfig();

            ProductUpgraderBase upgrader;
            upgrader = new GitHubReleasesUpgrader(ProcessHelper.GetCurrentProcessVersion(), tracer);

            return upgrader;
        }

        public abstract bool Initialize(out string errorMessage);

        public abstract bool TryGetNewerVersion(out Version newVersion, out string errorMessage);

        public abstract bool TryGetGitVersion(out GitVersion gitVersion, out string error);

        public abstract bool TryDownloadNewestVersion(out string errorMessage);

        public abstract bool TryRunGitInstaller(out bool installationSucceeded, out string error);

        public abstract bool TryRunGVFSInstaller(out bool installationSucceeded, out string error);

        public abstract bool TryCleanup(out string error);

        // TrySetupToolsDirectory -
        // Copies GVFS Upgrader tool and its dependencies to a temporary location in ProgramData.
        // Reason why this is needed - When GVFS.Upgrader.exe is run from C:\ProgramFiles\GVFS folder
        // upgrade installer that is downloaded and run will fail. This is because it cannot overwrite
        // C:\ProgramFiles\GVFS\GVFS.Upgrader.exe that is running. Moving GVFS.Upgrader.exe along with
        // its dependencies to a temporary location inside ProgramData and running GVFS.Upgrader.exe 
        // from this temporary location helps avoid this problem.
        public virtual bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            string rootDirectoryPath = ProductUpgrader.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);
            Exception exception;
            if (TryCreateDirectory(toolsDirectoryPath, out exception))
            {
                string currentPath = ProcessHelper.GetCurrentProcessLocation();
                error = null;
                foreach (string name in UpgraderToolAndLibs)
                {
                    string toolPath = Path.Combine(currentPath, name);
                    string destinationPath = Path.Combine(toolsDirectoryPath, name);
                    try
                    {
                        File.Copy(toolPath, destinationPath, overwrite: true);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        error = string.Join(
                            Environment.NewLine,
                            "File copy error - " + e.Message,
                            $"Make sure you have write permissions to directory {rootDirectoryPath} and run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} again.");
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

                upgraderToolPath = string.IsNullOrEmpty(error) ? Path.Combine(toolsDirectoryPath, UpgraderToolName) : null;
                return string.IsNullOrEmpty(error);
            }

            upgraderToolPath = null;
            error = exception.Message;
            this.TraceException(exception, nameof(this.TrySetupToolsDirectory), $"Error creating upgrade tools directory {toolsDirectoryPath}.");
            return false;
        }

        protected static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
        }

        protected static string GetLogDirectoryPath()
        {
            return Path.Combine(Paths.GetServiceDataRoot(RootDirectory), LogDirectory);
        }

        protected static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                DownloadDirectory);
        }

        protected static string GetTempPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                "InstallerTemp");
        }

        protected static bool TryCreateDirectory(string path, out Exception exception)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException e)
            {
                exception = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
                return false;
            }

            exception = null;
            return true;
        }

        protected void TraceException(Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            this.Tracer.RelatedError(metadata, message, Keywords.Telemetry);
        }

        protected virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }

        protected bool TryDeleteDirectory(string path, out Exception exception)
        {
            try
            {
                this.FileSystem.DeleteDirectory(path);
            }
            catch (IOException e)
            {
                exception = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
                return false;
            }

            exception = null;
            return true;
        }
    }
}
