using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public class LocalUpgraderServices
    {
        protected PhysicalFileSystem fileSystem;
        protected ITracer tracer;

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
                "Newtonsoft.Json.dll"
            };

        public LocalUpgraderServices(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            this.fileSystem = fileSystem;
            this.tracer = tracer;
            this.TempPath = LocalUpgraderServices.GetTempPath();
        }

        public string TempPath { get; }

        public static string GetTempPath()
        {
            return Path.Combine(
                ProductUpgraderInfo.GetUpgradesDirectoryPath(),
                "InstallerTemp");
        }

        // TrySetupToolsDirectory -
        // Copies GVFS Upgrader tool and its dependencies to a temporary location in ProgramData.
        // Reason why this is needed - When GVFS.Upgrader.exe is run from C:\ProgramFiles\GVFS folder
        // upgrade installer that is downloaded and run will fail. This is because it cannot overwrite
        // C:\ProgramFiles\GVFS\GVFS.Upgrader.exe that is running. Moving GVFS.Upgrader.exe along with
        // its dependencies to a temporary location inside ProgramData and running GVFS.Upgrader.exe
        // from this temporary location helps avoid this problem.
        public virtual bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            string rootDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);
            Exception exception;
            if (!this.fileSystem.TryCreateDirectory(toolsDirectoryPath, out exception))
            {
                upgraderToolPath = null;
                error = exception.Message;
                this.TraceException(exception, nameof(this.TrySetupToolsDirectory), $"Error creating upgrade tools directory {toolsDirectoryPath}.");
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

        public virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }

        protected void TraceException(Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            this.tracer.RelatedError(metadata, message, Keywords.Telemetry);
        }
    }
}
