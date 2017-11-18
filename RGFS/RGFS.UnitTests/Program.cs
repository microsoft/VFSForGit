using RGFS.Tests;
using RGFS.UnitTests.Category;
using System;
using System.Diagnostics;

namespace RGFS.UnitTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            NUnitRunner runner = new NUnitRunner(args);

            if (Debugger.IsAttached)
            {
                runner.ExcludeCategory(CategoryConstants.ExceptionExpected);
            }
            
            Environment.ExitCode = runner.RunTests(1);

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}