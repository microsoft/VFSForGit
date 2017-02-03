using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class TestConstants
    {
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
                public static readonly string Root = Path.Combine(DotGit.Root, "info");
                public static readonly string SparseCheckout = Path.Combine(Root, "sparse-checkout");
            }
        }
    }
}
