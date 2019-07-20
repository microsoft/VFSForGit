using GVFS.Common;
using GVFS.Platform.Mac;
using GVFS.Tests.Should;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Platform.Mac
{
    [TestFixture]
    public class MacServiceProcessTests
    {
        [TestCase]
        public void CanGetServices()
        {
            Mock<IProcessRunner> processHelperMock = new Mock<IProcessRunner>(MockBehavior.Strict);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PID\tStatus\tLabel");
            sb.AppendLine("1\t0\tcom.apple.process1");
            sb.AppendLine("2\t0\tcom.apple.process2");
            sb.AppendLine("3\t0\tcom.apple.process3");
            sb.AppendLine("-\t0\tcom.apple.process4");

            ProcessResult processResult = new ProcessResult(sb.ToString(), string.Empty, 0);

            processHelperMock.Setup(m => m.Run("/bin/launchctl", "asuser 521 /bin/launchctl list", true)).Returns(processResult);

            MacDaemonController daemonController = new MacDaemonController(processHelperMock.Object);
            bool success = daemonController.TryGetDaemons("521", out List<MacDaemonController.DaemonInfo> daemons, out string error);

            success.ShouldBeTrue();
            daemons.ShouldNotBeNull();
            daemons.Count.ShouldEqual(4);
            processHelperMock.VerifyAll();
        }
    }
}
