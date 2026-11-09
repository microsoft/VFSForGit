using GVFS.Common.NamedPipes;
using GVFS.Hooks;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class GitCommandLockTests
    {
        [TestCase(new[] { "post-command", "status", "--no-lock-index" }, null, true)]
        [TestCase(new[] { "post-command", "status", "--NO-LOCK-INDEX" }, null, true)]
        [TestCase(new[] { "pre-command", "status" }, "0", true)]
        [TestCase(new[] { "pre-command", "status" }, "Off", true)]
        [TestCase(new[] { "pre-command", "status" }, "1", false)]
        [TestCase(new[] { "pre-command", "status" }, null, false)]
        [TestCase(new[] { "post-command", "git-status", "--no-lock-index" }, "0", false)]
        [TestCase(new[] { "post-command", "STATUS", "--no-lock-index" }, "0", false)]
        [TestCase(new[] { "post-command", "log", "--no-lock-index" }, "0", false)]
        public void OnlyCanonicalStatusWithoutLocksIsAvailabilityOnly(string[] args, string optionalLocks, bool expected)
        {
            GitCommandLock.IsAvailabilityOnly(
                args,
                name => name == "GIT_OPTIONAL_LOCKS" ? optionalLocks : null).ShouldEqual(expected);
        }

        [TestCase(123, true)]
        [TestCase(456, false)]
        public void OwnershipComparesTheTypedPidWithoutAcquiring(int ownerPid, bool expectedOwner)
        {
            int requests = 0;
            GitCommandLock.TryCheckOwnership(
                123,
                isElevated: false,
                "git race",
                request =>
                {
                    ++requests;
                    request.Header.ShouldEqual("AcquireLock");
                    NamedPipeMessages.LockData data = new NamedPipeMessages.LockRequest(request.Body).RequestData;
                    data.PID.ShouldEqual(123);
                    data.CheckAvailabilityOnly.ShouldBeTrue();
                    data.ParsedCommand.ShouldEqual("git race");
                    return new NamedPipeMessages.AcquireLock.Response(
                        "LockDeniedGit",
                        new NamedPipeMessages.LockData(ownerPid, false, false, "git original", string.Empty)).CreateMessage();
                },
                out bool ownsLock,
                out string error).ShouldBeTrue();

            requests.ShouldEqual(1);
            ownsLock.ShouldEqual(expectedOwner);
            error.ShouldBeNull();
        }

        [TestCase("LockAvailable")]
        [TestCase("LockDeniedGVFS")]
        public void UnownedLocksNeedNoRelease(string result)
        {
            GitCommandLock.TryCheckOwnership(
                123, false, "git alias",
                request => new NamedPipeMessages.Message(result, null),
                out bool ownsLock,
                out string error).ShouldBeTrue();

            ownsLock.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase("MountNotReady", "not finished initializing")]
        [TestCase("UnmountInProgress", "unmounting")]
        [TestCase("LockAcquired", "unexpected lock-ownership response")]
        [TestCase("UnknownResponse", "unexpected lock-ownership response")]
        [TestCase("LockDeniedGit", "without a valid process ID")]
        public void InvalidOwnershipResponsesProduceADiagnostic(string result, string expectedError)
        {
            GitCommandLock.TryCheckOwnership(
                123, false, "git alias",
                request => new NamedPipeMessages.Message(result, null),
                out bool ownsLock,
                out string error).ShouldBeFalse();

            ownsLock.ShouldBeFalse();
            error.ShouldContain(expectedError);
        }

        [TestCase]
        public void MissingOwnershipResponseProducesADiagnostic()
        {
            GitCommandLock.TryCheckOwnership(
                123, false, "git alias", request => null,
                out bool ownsLock,
                out string error).ShouldBeFalse();

            ownsLock.ShouldBeFalse();
            error.ShouldContain("no lock-ownership response");
        }

        [TestCase("malformed")]
        [TestCase("123|false|false|-1|x|0|")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void MalformedOwnershipResponseProducesADiagnostic(string body)
        {
            GitCommandLock.TryCheckOwnership(
                123, false, "git alias",
                request => new NamedPipeMessages.Message("LockDeniedGit", body),
                out bool ownsLock,
                out string error).ShouldBeFalse();

            ownsLock.ShouldBeFalse();
            error.ShouldContain("invalid lock-ownership response");
        }
    }
}
