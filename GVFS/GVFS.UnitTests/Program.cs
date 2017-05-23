using GVFS.Tests;
using GVFS.UnitTests.Category;
using System;
using System.Diagnostics;

namespace GVFS.UnitTests
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
        }
    }
}