namespace GVFS.FunctionalTests
{
    public static class GVFSTestConfig
    {
        public static string RepoToClone { get; set; }

        public static bool NoSharedCache { get; set; }

        public static string LocalCacheRoot { get; set; }
        
        public static bool UseAllRunners { get; set; }
    }
}
