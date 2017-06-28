using GVFS.FunctionalTests.Category;
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

            if (runner.HasCustomArg("--full-suite"))
            {
                Console.WriteLine("Running the full suite of tests");
                FileSystemRunners.FileSystemRunner.UseAllRunners = true;
            }
            
            if (!runner.HasCustomArg("--run-builds"))
            {
                runner.ExcludeCategory(CategoryConstants.Build);
            }
            
            string servicePath = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFSService);
            GVFSServiceProcess.InstallService(servicePath);
            try
            {
                Environment.ExitCode = runner.RunTests(Properties.Settings.Default.TestRepeatCount);
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

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}