namespace GVFS.FunctionalTests
{
    public static class Categories
    {
        public const string FullSuiteOnly = "FullSuiteOnly";
        public const string FastFetch = "FastFetch";
        public const string GitCommands = "GitCommands";

        public const string Windows = "Windows";

        public static class Mac
        {
            public const string M1 = "M1_CloneAndMount";
            public const string M2 = "M2_StaticViewGitCommands";
            public const string M2TODO = "M2_StaticViewGitCommandsStillTODO";
            public const string M3 = "M3_AllGitCommands";
            public const string M4 = "M4_All";
        }
    }
}
