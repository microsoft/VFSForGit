using GVFS.Common;
using GVFS.Tests;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatform.Register(new MockPlatform());
            NUnitRunner runner = new NUnitRunner(args);
            runner.AddGlobalSetupIfNeeded("GVFS.UnitTests.Setup");

            List<string> excludeCategories = new List<string>();

            if (Debugger.IsAttached)
            {
                excludeCategories.Add(CategoryConstants.ExceptionExpected);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                excludeCategories.Add(CategoryConstants.CaseInsensitiveFileSystemOnly);
            }
            else
            {
                excludeCategories.Add(CategoryConstants.CaseSensitiveFileSystemOnly);
            }

            Environment.ExitCode = runner.RunTests(includeCategories: null, excludeCategories: excludeCategories);

            if (Debugger.IsAttached)
            {
                Console.WriteLine("Tests completed. Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}
