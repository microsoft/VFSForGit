using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests
{
    public static class GVFSTestConfig
    {
        public static string RepoToClone { get; set; }

        public static bool NoSharedCache { get; set; }

        public static string LocalCacheRoot { get; set; }
        
        public static bool UseAllRunners { get; set; }

        public static bool TestGVFSOnPath { get; set; }

        public static string PathToGVFS
        {
            get
            {
                return 
                    TestGVFSOnPath ? 
                    Properties.Settings.Default.PathToGVFS : 
                    Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            }
        }
    }
}
