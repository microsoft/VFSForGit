using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Properties
{
    public static class Settings
    {
        public static class Default
        {
            public static string CurrentDirectory { get; private set; }

            public static string RepoToClone { get; set; }
            public static string PathToBash { get; set; }
            public static string PathToGVFS { get; set; }
            public static string Commitish { get; set; }
            public static string ControlGitRepoRoot { get; set; }
            public static string EnlistmentRoot { get; set; }
            public static string FastFetchRoot { get; set; }
            public static string FastFetchControl { get; set; }
            public static string PathToGit { get; set; }
            public static string PathToGVFSService { get; set; }
            public static string BinaryFileNameExtension { get; set; }

            public static void Initialize()
            {
                CurrentDirectory = Path.GetFullPath(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]));

                RepoToClone = @"https://gvfs.visualstudio.com/ci/_git/ForTests";
                Commitish = @"FunctionalTests/20180214";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    EnlistmentRoot = @"C:\Repos\GVFSFunctionalTests\enlistment";
                    PathToGVFS = @"GVFS.exe";
                    PathToGit = @"C:\Program Files\Git\cmd\git.exe";
                    PathToBash = @"C:\Program Files\Git\bin\bash.exe";

                    ControlGitRepoRoot = @"C:\Repos\GVFSFunctionalTests\ControlRepo";
                    FastFetchRoot = @"C:\Repos\GVFSFunctionalTests\FastFetch\Test";
                    FastFetchControl = @"C:\Repos\GVFSFunctionalTests\FastFetch\Control";
                    PathToGVFSService = @"GVFS.Service.exe";
                    BinaryFileNameExtension = ".exe";
                }
                else
                {
                    string root = "/GVFS.FT";
                    EnlistmentRoot = Path.Combine(root, "test");
                    ControlGitRepoRoot = Path.Combine(root, "control");
                    FastFetchRoot = Path.Combine(root, "FFtest");
                    FastFetchControl = Path.Combine(root, "FFcontrol");
                    PathToGVFS = "gvfs";
                    PathToGit = "/usr/local/bin/git";
                    PathToBash = "/bin/bash";
                    BinaryFileNameExtension = string.Empty;
                }
            }
        }
    }
}
