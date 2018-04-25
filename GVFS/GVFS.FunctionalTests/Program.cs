using GVFS.FunctionalTests.Tests;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
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

            GVFSTestConfig.LocalCacheRoot = runner.GetCustomArgWithParam("--shared-gvfs-cache-root");

            if (runner.HasCustomArg("--full-suite"))
            {
                Console.WriteLine("Running the full suite of tests");
                GVFSTestConfig.UseAllRunners = true;
            }
            else
            {
                runner.ExcludeCategory(Categories.FullSuiteOnly);
            }

            GVFSTestConfig.RepoToClone =
                runner.GetCustomArgWithParam("--repo-to-clone")
                ?? Properties.Settings.Default.RepoToClone;

            string servicePath = 
                GVFSTestConfig.TestGVFSOnPath ? 
                Properties.Settings.Default.PathToGVFSService : 
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFSService);

            GVFSServiceProcess.InstallService(servicePath);
            try
            {
                Environment.ExitCode = runner.RunTests();
            }
            finally
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

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}