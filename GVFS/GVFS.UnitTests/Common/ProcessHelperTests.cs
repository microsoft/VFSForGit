using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class ProcessHelperTests
    {
        [TestCase]
        public void RunClosesStandardInputOfTheChild()
        {
            // With no file arguments, findstr reads standard input until end-of-file.
            string findstr = Path.Combine(Environment.SystemDirectory, "findstr.exe");
            Task<ProcessResult> run = Task.Run(() => ProcessHelper.Run(findstr, "gvfs"));

            run.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue("findstr waited for standard input");
            run.Result.ExitCode.ShouldEqual(1);
        }
    }
}
