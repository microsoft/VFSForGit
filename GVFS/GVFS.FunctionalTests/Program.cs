using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Properties.Settings.Default.Initialize();
            NUnitRunner runner = new NUnitRunner(args);
            runner.AddGlobalSetupIfNeeded("GVFS.FunctionalTests.GlobalSetup");

            if (runner.HasCustomArg("--no-shared-gvfs-cache"))
            {
                Console.WriteLine("Running without a shared git object cache");
                GVFSTestConfig.NoSharedCache = true;
            }

            if (runner.HasCustomArg("--test-gvfs-on-path"))
            {
                Console.WriteLine("Running tests against GVFS on path");
                GVFSTestConfig.TestGVFSOnPath = true;
            }

            if (runner.HasCustomArg("--replace-inbox-projfs"))
            {
                Console.WriteLine("Tests will replace inbox ProjFS");
                GVFSTestConfig.ReplaceInboxProjFS = true;
            }

            GVFSTestConfig.LocalCacheRoot = runner.GetCustomArgWithParam("--shared-gvfs-cache-root");

            HashSet<string> includeCategories = new HashSet<string>();
            HashSet<string> excludeCategories = new HashSet<string>();

            if (runner.HasCustomArg("--full-suite"))
            {
                Console.WriteLine("Running the full suite of tests");

                List<object[]> modes = new List<object[]>();
                foreach (Settings.ValidateWorkingTreeMode mode in Enum.GetValues(typeof(Settings.ValidateWorkingTreeMode)))
                {
                    modes.Add(new object[] { mode });
                }

                GVFSTestConfig.GitRepoTestsValidateWorkTree = modes.ToArray();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.AllWindowsRunners;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.AllMacRunners;
                }
            }
            else
            {
                Settings.ValidateWorkingTreeMode validateMode = Settings.ValidateWorkingTreeMode.Full;

                if (runner.HasCustomArg("--sparse-mode"))
                {
                    validateMode = Settings.ValidateWorkingTreeMode.SparseMode;

                    // Only test the git commands in sparse mode for splitting out tests in builds
                    includeCategories.Add(Categories.GitCommands);
                }

                GVFSTestConfig.GitRepoTestsValidateWorkTree =
                    new object[]
                    {
                        new object[] { validateMode },
                    };

                if (runner.HasCustomArg("--extra-only"))
                {
                    Console.WriteLine("Running only the tests marked as ExtraCoverage");
                    includeCategories.Add(Categories.ExtraCoverage);
                }
                else
                {
                    excludeCategories.Add(Categories.ExtraCoverage);
                }

                GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.DefaultRunners;
            }

            if (runner.HasCustomArg("--windows-only"))
            {
                includeCategories.Add(Categories.WindowsOnly);

                // RunTests unions all includeCategories.  Remove ExtraCoverage to
                // ensure that we only run tests flagged as WindowsOnly
                includeCategories.Remove(Categories.ExtraCoverage);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                excludeCategories.Add(Categories.MacTODO.NeedsNewFolderCreateNotification);
                excludeCategories.Add(Categories.MacTODO.NeedsGVFSConfig);
                excludeCategories.Add(Categories.MacTODO.NeedsStatusCache);
                excludeCategories.Add(Categories.MacTODO.TestNeedsToLockFile);
                excludeCategories.Add(Categories.WindowsOnly);
            }
            else
            {
                // Windows excludes.
                excludeCategories.Add(Categories.MacOnly);
                excludeCategories.Add(Categories.POSIXOnly);
            }

            GVFSTestConfig.DotGVFSRoot = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".vfsforgit" : ".gvfs";

            GVFSTestConfig.RepoToClone =
                runner.GetCustomArgWithParam("--repo-to-clone")
                ?? Properties.Settings.Default.RepoToClone;

            RunBeforeAnyTests();
            Environment.ExitCode = runner.RunTests(includeCategories, excludeCategories);

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }

        private static void RunBeforeAnyTests()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (GVFSTestConfig.ReplaceInboxProjFS)
                {
                    ProjFSFilterInstaller.ReplaceInboxProjFS();
                }

                GVFSServiceProcess.InstallService();

                string statusCacheVersionTokenPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                    "GVFS",
                    "GVFS.Service",
                    "EnableGitStatusCacheToken.dat");

                if (!File.Exists(statusCacheVersionTokenPath))
                {
                    File.WriteAllText(statusCacheVersionTokenPath, string.Empty);
                }
            }
        }
    }
}
