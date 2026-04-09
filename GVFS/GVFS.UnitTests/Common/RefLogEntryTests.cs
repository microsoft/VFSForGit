using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RefLogEntryTests
    {
        [TestCase]
        public void ParsesValidRefLog()
        {
            const string SourceSha = "0000000000000000000000000000000000000000";
            const string TargetSha = "d249e0fea84484eb105d52174cf326958ee87ab4";
            const string Reason = "clone: from https://repourl";
            string testLine = string.Format("{0} {1} author <user@microsoft.com> 1478738341 -0800\t{2}", SourceSha, TargetSha, Reason);

            RefLogEntry output;
            RefLogEntry.TryParse(testLine, out output).ShouldEqual(true);

            output.ShouldNotBeNull();
            output.SourceSha.ShouldEqual(SourceSha);
            output.TargetSha.ShouldEqual(TargetSha);
            output.Reason.ShouldEqual(Reason);
        }

        [TestCase]
        public void FailsForMissingReason()
        {
            const string SourceSha = "0000000000000000000000000000000000000000";
            const string TargetSha = "d249e0fea84484eb105d52174cf326958ee87ab4";
            string testLine = string.Format("{0} {1} author <user@microsoft.com> 1478738341 -0800", SourceSha, TargetSha);

            RefLogEntry output;
            RefLogEntry.TryParse(testLine, out output).ShouldEqual(false);

            output.ShouldBeNull();
        }

        [TestCase]
        public void FailsForMissingTargetSha()
        {
            const string SourceSha = "0000000000000000000000000000000000000000";
            string testLine = string.Format("{0} ", SourceSha);

            RefLogEntry output;
            RefLogEntry.TryParse(testLine, out output).ShouldEqual(false);

            output.ShouldBeNull();
        }

        [TestCase]
        public void FailsForNull()
        {
            string testLine = null;

            RefLogEntry output;
            RefLogEntry.TryParse(testLine, out output).ShouldEqual(false);

            output.ShouldBeNull();
        }
    }
}
