using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using PrjFSLib.Mac;
using System;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    public class ProjFSKext : IKernelDriver
    {
        private const string DriverName = "io.gvfs.PrjFSKext";

        public bool EnumerationExpandsDirectories { get; } = true;

        public string LogsFolderPath => throw new NotImplementedException();

        public bool IsGVFSUpgradeSupported()
        {
            // TODO(Mac)
            return false;
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

        public string FlushLogs()
        {
            throw new NotImplementedException();
        }

        public bool IsReady(JsonTracer tracer, string enlistmentRoot, out string error)
        {
            ProcessResult loadedKexts = ProcessHelper.Run("kextstat", args: "-b " + DriverName, redirectOutput: true);
            if (loadedKexts.Output.Contains(DriverName))
            {
                error = null;
                return true;
            }

            error = DriverName + " is not loaded. Make sure the driver is loaded and try again.";
            return false;
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
    }
}
