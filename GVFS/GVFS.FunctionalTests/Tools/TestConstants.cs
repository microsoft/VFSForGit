using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class TestConstants
    {
        public const string AllZeroSha = "0000000000000000000000000000000000000000";
        public const string PartialFolderPlaceholderDatabaseValue = "                          PARTIAL FOLDER";
        public const char GitPathSeparator = '/';
        public const string InternalUseOnlyFlag = "--internal_use_only";

        public static class DotGit
        {
            public const string Root = ".git";
            public static readonly string Head = Path.Combine(DotGit.Root, "HEAD");

            public static class Objects
            {
                public static readonly string Root = Path.Combine(DotGit.Root, "objects");
            }

            public static class Info
            {
                public const string Name = "info";
                public const string AlwaysExcludeName = "always_exclude";
                public const string SparseCheckoutName = "sparse-checkout";

                public static readonly string Root = Path.Combine(DotGit.Root, Info.Name);
                public static readonly string SparseCheckoutPath = Path.Combine(Info.Root, Info.SparseCheckoutName);
                public static readonly string AlwaysExcludePath = Path.Combine(Info.Root, AlwaysExcludeName);
            }
        }

        public static class Databases
        {
            public const string Root = "databases";
            public static readonly string BackgroundOpsFile = Path.Combine(Root, "BackgroundGitOperations.dat");
            public static readonly string ModifiedPaths = Path.Combine(Root, "ModifiedPaths.dat");
            public static readonly string VFSForGit = Path.Combine(Root, "VFSForGit.sqlite");
        }
    }
}
