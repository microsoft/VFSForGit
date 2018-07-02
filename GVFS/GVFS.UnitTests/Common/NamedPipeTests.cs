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
        [Category(CategoryConstants.ExceptionExpected)]
        public void LockData_FromBody_Parsing()
        {
            // Verify simple vanilla parsing
            LockData lockDataBefore = new LockData(1, true, true, "git status --serialize=D:\\Sources\\tqoscy2l.ud0_status.tmp --ignored=matching --untracked-files=complete");
            LockData lockDataAfter = LockData.FromBody(lockDataBefore.ToMessage());
            lockDataAfter.PID.ShouldEqual(1);
            lockDataAfter.IsElevated.ShouldEqual(true);
            lockDataAfter.CheckAvailabilityOnly.ShouldEqual(true);
            lockDataAfter.ParsedCommand.ShouldEqual("git status --serialize=D:\\Sources\\tqoscy2l.ud0_status.tmp --ignored=matching --untracked-files=complete");

            // Verify strings with "|" will work
            LockData lockDataWithPipeBefore = new LockData(1, true, true, "git commit -m 'message with a | and another |'");
            LockData lockDataWithPipeAfter = LockData.FromBody(lockDataWithPipeBefore.ToMessage());
            lockDataWithPipeAfter.PID.ShouldEqual(1);
            lockDataWithPipeAfter.IsElevated.ShouldEqual(true);
            lockDataWithPipeAfter.CheckAvailabilityOnly.ShouldEqual(true);
            lockDataWithPipeAfter.ParsedCommand.ShouldEqual("git commit -m 'message with a | and another |'");

            // Verify exception cases and messages
            this.ValidateError("1|true|true", "Invalid lock message. Expected at least 5 parts, got: 3 from message: '1|true|true'");
            this.ValidateError("blah|true|true|10|git status", "Invalid lock message. Expected PID, got: blah from message: 'blah|true|true|10|git status'");
            this.ValidateError("1|true|1|10|git status", "Invalid lock message. Expected bool for checkAvailabilityOnly, got: 1 from message: '1|true|1|10|git status'");
            this.ValidateError("1|1|true|10|git status", "Invalid lock message. Expected bool for isElevated, got: 1 from message: '1|1|true|10|git status'");
            this.ValidateError("1|true|true|true|git status", "Invalid lock message. Expected command length, got: true from message: '1|true|true|true|git status'");
            this.ValidateError("1|true|true|5|git status", "Invalid lock message. The parsedCommand is an unexpected length, got: 5 from message: '1|true|true|5|git status'");
        }

        private void ValidateError(string body, string exceptionMessage)
        {
            Assert.Throws<InvalidOperationException>(() => LockData.FromBody(body)).Message.Equals(exceptionMessage);
        }
    }
}
