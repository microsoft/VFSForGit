using GVFS.FunctionalTests.Tests;
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

                GVFSTestConfig.GitRepoTestsValidateWorkTree = DataSources.AllBools;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.AllWindowsRunners;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.AllLinuxRunners;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    GVFSTestConfig.FileSystemRunners = FileSystemRunners.FileSystemRunner.AllMacRunners;
                }
            }
            else
            {
                GVFSTestConfig.GitRepoTestsValidateWorkTree =
                    new object[]
                    {
                        new object[] { true }
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                excludeCategories.Add(Categories.WindowsOnly);
            }
            else
            {
                excludeCategories.Add(Categories.LinuxOnly);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                excludeCategories.Add(Categories.MacTODO.FailsOnBuildAgent);
                excludeCategories.Add(Categories.MacTODO.NeedsNewFolderCreateNotification);
                excludeCategories.Add(Categories.MacTODO.NeedsGVFSConfig);
                excludeCategories.Add(Categories.MacTODO.NeedsDehydrate);
                excludeCategories.Add(Categories.MacTODO.NeedsServiceVerb);
                excludeCategories.Add(Categories.MacTODO.NeedsStatusCache);
                excludeCategories.Add(Categories.MacTODO.NeedsCorruptObjectFix);
                excludeCategories.Add(Categories.MacTODO.TestNeedsToLockFile);
                excludeCategories.Add(Categories.WindowsOnly);
            }
            else
            {
                excludeCategories.Add(Categories.MacOnly);
            }

            GVFSTestConfig.DotGVFSRoot = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".vfsforgit" : ".gvfs";

            GVFSTestConfig.RepoToClone =
                runner.GetCustomArgWithParam("--repo-to-clone")
                ?? Properties.Settings.Default.RepoToClone;

            RunBeforeAnyTests();
            Environment.ExitCode = runner.RunTests(includeCategories, excludeCategories);
            RunAfterAllTests();

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

        private static void RunAfterAllTests()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string serviceLogFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "GVFS",
                    GVFSServiceProcess.TestServiceName,
                    "Logs");

                Console.WriteLine("GVFS.Service logs at '{0}' attached below.\n\n", serviceLogFolder);
                foreach (string filename in TestResultsHelper.GetAllFilesInDirectory(serviceLogFolder))
                {
                    TestResultsHelper.OutputFileContents(filename);
                }

                GVFSServiceProcess.UninstallService();
            }

            PrintTestCaseStats.PrintRunTimeStats();
        }
    }
}
