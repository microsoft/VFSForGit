using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Properties
{
    public static class Settings
    {
        public enum ValidateWorkingTreeMode
        {
            None = 0,
            Full = 1,
            SparseMode = 2,
        }

        public static class Default
        {
            public static string CurrentDirectory { get; private set; }

            public static string RepoToClone { get; set; }
            public static string PathToBash { get; set; }
            public static string PathToGVFS { get; set; }
            public static string Commitish { get; set; }
            public static string ControlGitRepoRoot { get; set; }
            public static string EnlistmentRoot { get; set; }
            public static string FastFetchBaseRoot { get; set; }
            public static string FastFetchRoot { get; set; }
            public static string FastFetchControl { get; set; }
            public static string PathToGit { get; set; }
            public static string PathToGVFSService { get; set; }
            public static string BinaryFileNameExtension { get; set; }

            public static void Initialize()
            {
                string testExec = System.Reflection.Assembly.GetEntryAssembly().Location;
                CurrentDirectory = Path.GetFullPath(Path.GetDirectoryName(testExec));

                RepoToClone = @"https://gvfs.visualstudio.com/ci/_git/ForTests";

                // HACK: This is only different from FunctionalTests/20180214
                // in that it deletes the GVFlt_MoveFileTests/LongFileName folder,
                // which is causing problems in all tests due to a ProjFS
                // regression. Replace this with the expected default after
                // ProjFS is fixed and deployed to our build machines.
                Commitish = @"FunctionalTests/20201014";

                EnlistmentRoot = @"C:\Repos\GVFSFunctionalTests\enlistment";
                PathToGVFS = @"C:\Program Files\VFS for Git\GVFS.exe";
                PathToGit = @"C:\Program Files\Git\cmd\git.exe";
                if (Environment.GetEnvironmentVariable("DDDGIT") != null)
                {
                    PathToGit = Environment.GetEnvironmentVariable("DDDGIT");
                }
                PathToBash = @"C:\Program Files\Git\bin\bash.exe";

                ControlGitRepoRoot = @"C:\Repos\GVFSFunctionalTests\ControlRepo";
                FastFetchBaseRoot = @"C:\Repos\GVFSFunctionalTests\FastFetch";
                FastFetchRoot = Path.Combine(FastFetchBaseRoot, "test");
                FastFetchControl = Path.Combine(FastFetchBaseRoot, "control");
                PathToGVFSService = @"C:\Program Files\VFS for Git\GVFS.Service.exe";
                BinaryFileNameExtension = ".exe";
            }
        }
    }
}
