using GVFS.FunctionalTests.Tests;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests
{
    [SetUpFixture]
    public class GlobalSetup
    {
        [OneTimeSetUp]
        public void RunBeforeAnyTests()
        {
        }

        [OneTimeTearDown]
        public void RunAfterAllTests()
        {
            PrintTestCaseStats.PrintRunTimeStats();
        }
    }
}
