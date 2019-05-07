using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using PrjFSLib.Mac;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    public class ProjFSKext : IKernelDriver
    {
        private const string DriverName = "org.vfsforgit.PrjFSKext";
        private const int LoadKext_ExitCode_Success = 0;

        // This exit code was found in the following article
        // https://developer.apple.com/library/archive/technotes/tn2459/_index.html
        private const int LoadKext_ExitCode_NotApproved = 27;

        public bool EnumerationExpandsDirectories { get; } = true;

        public string LogsFolderPath
        {
            get
            {
                return Path.Combine(System.IO.Path.GetTempPath(), "PrjFSKext");
            }
        }

        public bool IsGVFSUpgradeSupported()
        {
            return true;
        }

        public bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error)
        {
            warning = null;
            error = null;

            string pathRoot = Path.GetPathRoot(normalizedEnlistmentRootPath);
            DriveInfo rootDriveInfo = DriveInfo.GetDrives().FirstOrDefault(x => x.Name == pathRoot);
            if (rootDriveInfo == null)
            {
                warning = $"Unable to ensure that '{normalizedEnlistmentRootPath}' is an APFS or HFS+ volume.";
            }
            else if (!string.Equals(rootDriveInfo.DriveFormat, "APFS", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(rootDriveInfo.DriveFormat, "HFS", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Error: Currently only APFS and HFS+ volumes are supported.  Ensure repo is located into an APFS or HFS+ volume.";
                return false;
            }

            return true;
        }

        public bool TryFlushLogs(out string error)
        {
            Directory.CreateDirectory(this.LogsFolderPath);
            ProcessResult logShowOutput = ProcessHelper.Run("log", args: "show --predicate \"subsystem contains \'org.vfsforgit\'\" --info", redirectOutput: true);
            File.WriteAllText(Path.Combine(this.LogsFolderPath, "PrjFSKext.log"), logShowOutput.Output);
            error = string.Empty;

            return true;
        }

        public bool IsReady(JsonTracer tracer, string enlistmentRoot, TextWriter output, out string error)
        {
            error = null;
            return
                this.IsKextLoaded() ||
                this.TryLoad(tracer, output, out error);
        }

        public bool TryPrepareFolderForCallbacks(string folderPath, out string error, out Exception exception)
        {
            exception = null;
            error = string.Empty;
            Result result = VirtualizationInstance.ConvertDirectoryToVirtualizationRoot(folderPath);
            if (result != Result.Success)
            {
                error = "Failed to prepare \"" + folderPath + "\" for callbacks, error: " + result.ToString("F");
                return false;
            }

            return true;
        }

        private bool TryLoad(ITracer tracer, TextWriter output, out string errorMessage)
        {
            output?.WriteLine("Driver not loaded.  Attempting to load. You may be prompted for sudo password...");
            EventMetadata metadata = new EventMetadata();
            ProcessResult loadKext = ProcessHelper.Run("sudo", "/sbin/kextload -b " + DriverName);
            if (loadKext.ExitCode == LoadKext_ExitCode_Success)
            {
                tracer.RelatedWarning(metadata, $"{DriverName} was successfully loaded but should have been autoloaded.", Keywords.Telemetry);
                errorMessage = null;
                return true;
            }
            else if (loadKext.ExitCode == LoadKext_ExitCode_NotApproved)
            {
                tracer.RelatedError("Kext unable to load. Not approved by the user");
                errorMessage = DriverName + @" was unable to load.  Please check and make sure you have allowed the extension in
System Preferences -> Security & Privacy";
            }
            else
            {
                metadata.Add("ExitCode", loadKext.ExitCode);
                metadata.Add("Output", loadKext.Output);
                metadata.Add("Errors", loadKext.Errors);
                tracer.RelatedError(metadata, "Failed to load kext");

                errorMessage = DriverName + " is not loaded. Make sure the kext is loaded and try again.";
            }

            return false;
        }

        private bool IsKextLoaded()
        {
            ProcessResult loadedKexts = ProcessHelper.Run("kextstat", args: "-b " + DriverName, redirectOutput: true);
            return loadedKexts.Output.Contains(DriverName);
        }
    }
}
