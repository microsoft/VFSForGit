using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using static GVFS.Common.NamedPipes.NamedPipeMessages;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NamedPipeTests
    {
        [TestCase]
        public void LockData_FromBody_Simple()
        {
            // Verify simple vanilla parsing
            LockData lockDataBefore = new LockData(1, true, true, "git status --serialize=D:\\Sources\\tqoscy2l.ud0_status.tmp --ignored=matching --untracked-files=complete", "123");
            LockData lockDataAfter = LockData.FromBody(lockDataBefore.ToMessage());
            lockDataAfter.PID.ShouldEqual(1);
            lockDataAfter.IsElevated.ShouldEqual(true);
            lockDataAfter.CheckAvailabilityOnly.ShouldEqual(true);
            lockDataAfter.ParsedCommand.ShouldEqual("git status --serialize=D:\\Sources\\tqoscy2l.ud0_status.tmp --ignored=matching --untracked-files=complete");
            lockDataAfter.GitCommandSessionId.ShouldEqual("123");
        }

        [TestCase]
        public void LockData_FromBody_WithDelimiters()
        {
            // Verify strings with "|" will work
            LockData lockDataWithPipeBefore = new LockData(1, true, true, "git commit -m 'message with a | and another |'", "123|321");
            LockData lockDataWithPipeAfter = LockData.FromBody(lockDataWithPipeBefore.ToMessage());
            lockDataWithPipeAfter.PID.ShouldEqual(1);
            lockDataWithPipeAfter.IsElevated.ShouldEqual(true);
            lockDataWithPipeAfter.CheckAvailabilityOnly.ShouldEqual(true);
            lockDataWithPipeAfter.ParsedCommand.ShouldEqual("git commit -m 'message with a | and another |'");
            lockDataWithPipeAfter.GitCommandSessionId.ShouldEqual("123|321");
        }

        [TestCase("1|true|true", "Invalid lock message. Expected at least 7 parts, got: 3 from message: '1|true|true'")]
        [TestCase("123|true|true|10|git status", "Invalid lock message. Expected at least 7 parts, got: 5 from message: '123|true|true|10|git status'")]
        [TestCase("blah|true|true|10|git status|9|sessionId", "Invalid lock message. Expected PID, got: blah from message: 'blah|true|true|10|git status|9|sessionId'")]
        [TestCase("1|true|1|10|git status|9|sessionId", "Invalid lock message. Expected bool for checkAvailabilityOnly, got: 1 from message: '1|true|1|10|git status|9|sessionId'")]
        [TestCase("1|1|true|10|git status|9|sessionId", "Invalid lock message. Expected bool for isElevated, got: 1 from message: '1|1|true|10|git status|9|sessionId'")]
        [TestCase("1|true|true|true|git status|9|sessionId", "Invalid lock message. Expected command length, got: true from message: '1|true|true|true|git status|9|sessionId'")]
        [TestCase("1|true|true|5|git status|9|sessionId", "Invalid lock message. Expected session id length, got: atus from message: '1|true|true|5|git status|9|sessionId'")]
        [TestCase("1|true|true|10|git status|bad|sessionId", "Invalid lock message. Expected session id length, got: bad from message: '1|true|true|10|git status|bad|sessionId'")]
        [TestCase("1|true|true|20|git status|9|sessionId", "Invalid lock message. Expected session id length, got: d from message: '1|true|true|20|git status|9|sessionId'")]
        [TestCase("1|true|true|10|git status|5|sessionId", "Invalid lock message. The sessionId is an unexpected length, got: 5 from message: '1|true|true|10|git status|5|sessionId'")]
        [TestCase("1|true|true|10|git status|20|sessionId", "Invalid lock message. The sessionId is an unexpected length, got: 20 from message: '1|true|true|10|git status|20|sessionId'")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void LockData_FromBody_Exception(string body, string exceptionMessage)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => LockData.FromBody(body));
            exception.Message.ShouldEqual(exceptionMessage);
        }
    }
}
