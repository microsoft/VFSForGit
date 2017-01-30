using GVFS.Tests;
using System;

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

            Environment.ExitCode = runner.RunTests(Properties.Settings.Default.TestRepeatCount);
        }
    }
}