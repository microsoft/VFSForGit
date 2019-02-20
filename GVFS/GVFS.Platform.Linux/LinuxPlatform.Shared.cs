using System;
using System.IO;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform
    {
        public static string GetDataRootForGVFSImplementation()
        {
            // TODO(Linux): determine installation location and data path
            string path = Environment.GetEnvironmentVariable("VFS4G_DATA_PATH");
            if (path == null)
            {
                path = "/var/run/vfsforgit";
            }

            return path;
        }

        public static string GetDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), componentName);
        }
    }
}
