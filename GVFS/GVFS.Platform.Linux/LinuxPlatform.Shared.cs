using System;
using System.IO;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform
    {
        public static string GetDataRootForGVFSImplementation()
        {
            // TODO(Linux): handle cases where env var or path do not exist?
            return Environment.GetEnvironmentVariable("VFSFORGIT_PATH");
        }

        public static string GetDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), componentName);
        }
    }
}
