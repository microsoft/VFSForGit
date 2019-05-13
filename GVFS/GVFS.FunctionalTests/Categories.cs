namespace GVFS.FunctionalTests
{
    public static class Categories
    {
        public const string ExtraCoverage = "ExtraCoverage";
        public const string FastFetch = "FastFetch";
        public const string GitCommands = "GitCommands";

        public const string WindowsOnly = "WindowsOnly";
        public const string LinuxOnly = "LinuxOnly";
        public const string MacOnly = "MacOnly";

        public static class MacTODO
        {
            // The FailsOnBuildAgent category is for tests that pass on dev
            // machines but not on the build agents
            public const string FailsOnBuildAgent = "FailsOnBuildAgent";

            // Tests that require #360 (detecting/handling new empty folders)
            public const string NeedsNewFolderCreateNotification = "NeedsNewFolderCreateNotification";

            // Tests for GVFS features that are not required for correct git functionality
            public const string M4 = "M4_GVFSFeatures";

            // Tests that have been flaky on build servers and need additional logging and\or
            // investigation
            public const string FlakyTest = "MacFlakyTest";
        }
    }
}
