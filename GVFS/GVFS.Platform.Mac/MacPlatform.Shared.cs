using System;
using System.IO;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform
    {
        public static string GetDataRootForGVFSImplementation()
        {
            return Path.Combine(
                Environment.GetEnvironmentVariable("HOME"),
                "Library",
                "Application Support",
                "GVFS");
        }

        public static string GetDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), componentName);
        }
    }
}
