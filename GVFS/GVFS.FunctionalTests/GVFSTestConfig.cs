using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests
{
    public static class GVFSTestConfig
    {
        public static string RepoToClone { get; set; }

        public static bool NoSharedCache { get; set; }

        public static string LocalCacheRoot { get; set; }
        
        public static object[] FileSystemRunners { get; set; }

        public static bool TestGVFSOnPath { get; set; }

        public static bool ReplaceInboxProjFS { get; set; }

        public static bool StartedFromDebugger { get; set; }

        public static string PathToGVFS
        {
            get
            {
                if (TestGVFSOnPath)
                {
                    return Properties.Settings.Default.PathToGVFS;
                }

                if (StartedFromDebugger && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return Path.Combine(Properties.Settings.Default.CurrentDirectory, "..", "..", "..", "..", "..", "..", "Publish", Properties.Settings.Default.PathToGVFS);
                }

                return Path.Combine(Properties.Settings.Default.CurrentDirectory, Properties.Settings.Default.PathToGVFS);
            }
        }
    }
}
