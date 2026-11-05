using GVFS.CommandLine;
using GVFS.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class MountVerbTests
    {
        [TestCase(0, ReturnCode.GenericError)]
        [TestCase(1, ReturnCode.ParsingError)]
        [TestCase(42, (ReturnCode)42)]
        public void GetMountFailureReturnCodeMapsChildExitCode(int childExitCode, ReturnCode expectedReturnCode)
        {
            Assert.That(MountVerb.GetMountFailureReturnCode(childExitCode), Is.EqualTo(expectedReturnCode));
        }
    }
}
