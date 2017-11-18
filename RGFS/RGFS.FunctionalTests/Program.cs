using RGFS.FunctionalTests.Tests;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace RGFS.FunctionalTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            NUnitRunner runner = new NUnitRunner(args);            

            if (runner.HasCustomArg("--full-suite"))
            {
                Console.WriteLine("Running the full suite of tests");
                RGFSTestConfig.UseAllRunners = true;
            }

            RGFSTestConfig.RepoToClone =
                runner.GetCustomArgWithParam("--repo-to-clone")
                ?? Properties.Settings.Default.RepoToClone;
            
            string servicePath = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFSService);
            RGFSServiceProcess.InstallService(servicePath);
            try
            {
                Environment.ExitCode = runner.RunTests(Properties.Settings.Default.TestRepeatCount);
            }
            finally
            {
                string serviceLogFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "RGFS",
                    RGFSServiceProcess.TestServiceName,
                    "Logs");

                Console.WriteLine("RGFS.Service logs at '{0}' attached below.\n\n", serviceLogFolder);
                foreach (string filename in TestResultsHelper.GetAllFilesInDirectory(serviceLogFolder))
                {
                    TestResultsHelper.OutputFileContents(filename);
                }

                RGFSServiceProcess.UninstallService();
            }

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}