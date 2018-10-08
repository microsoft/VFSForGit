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

            // Tests that require the LockHolder project to be converted to .NET Core (#150)
            public const string NeedsLockHolder = "NeedsDotCoreLockHolder";

            // Tests that require #356 (old paths to be delivered with rename notifications)
            public const string NeedsRenameOldPath = "NeedsRenameOldPath";

            // Git related tests that are not yet passing on Mac
            public const string M3 = "M3_AllGitCommands";

            // Tests for GVFS features that are not required for correct git functionality
            public const string M4 = "M4_GVFSFeatures";
        }
    }
}
