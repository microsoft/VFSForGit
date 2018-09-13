namespace GVFS.FunctionalTests
{
    public static class Categories
    {
        public const string FullSuiteOnly = "FullSuiteOnly";
        public const string FastFetch = "FastFetch";
        public const string GitCommands = "GitCommands";

        public const string WindowsOnly = "WindowsOnly";
        public const string MacOnly = "MacOnly";

        public static class MacTODO
        {
            // The FailsOnBuildAgent category is for tests that pass on dev
            // machines but not on the build agents
            public const string FailsOnBuildAgent = "FailsOnBuildAgent";
            public const string NeedsLockHolder = "NeedsDotCoreLockHolder";
            public const string M2 = "M2_StaticViewGitCommands";
            public const string M3 = "M3_AllGitCommands";
            public const string M4 = "M4_All";
        }
    }
}
