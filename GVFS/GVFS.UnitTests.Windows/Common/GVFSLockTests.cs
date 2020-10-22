using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Virtual;
using Moq;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSLockTests : TestsWithCommonRepo
    {
        private static readonly NamedPipeMessages.LockData DefaultLockData = new NamedPipeMessages.LockData(
            pid: 1234,
            isElevated: false,
            checkAvailabilityOnly: false,
            parsedCommand: "git command",
            gitCommandSessionId: "123");

        [TestCase]
        public void TryAcquireAndReleaseLockForExternalRequestor()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "ReleaseLockHeldByExternalProcess", It.IsAny<EventMetadata>(), Keywords.Telemetry));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);

            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID);
            this.ValidateLockIsFree(gvfsLock);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_WhenNoLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "ReleaseLockHeldByExternalProcess", It.IsAny<EventMetadata>(), Keywords.Telemetry));
            GVFSLock gvfsLock = new GVFSLock(mockTracer.Object);
            this.ValidateLockIsFree(gvfsLock);
            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID).ShouldBeFalse();
            this.ValidateLockIsFree(gvfsLock);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_DifferentPID()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "ReleaseLockHeldByExternalProcess", It.IsAny<EventMetadata>(), Keywords.Telemetry));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);
            gvfsLock.ReleaseLockHeldByExternalProcess(4321).ShouldBeFalse();
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_WhenGVFSHasLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "ReleaseLockHeldByExternalProcess", It.IsAny<EventMetadata>(), Keywords.Telemetry));
            GVFSLock gvfsLock = this.AcquireGVFSLock(mockTracer.Object);

            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID).ShouldBeFalse();
            this.ValidateLockHeldByGVFS(gvfsLock);
            mockTracer.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReleaseLockHeldByGVFS_WhenNoLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            GVFSLock gvfsLock = new GVFSLock(mockTracer.Object);
            this.ValidateLockIsFree(gvfsLock);
            Assert.Throws<InvalidOperationException>(() => gvfsLock.ReleaseLockHeldByGVFS());
            mockTracer.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReleaseLockHeldByGVFS_WhenExternalHasLockShouldThrow()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);

            Assert.Throws<InvalidOperationException>(() => gvfsLock.ReleaseLockHeldByGVFS());
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void TryAcquireLockForGVFS()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "ReleaseLockHeldByGVFS", It.IsAny<EventMetadata>()));
            GVFSLock gvfsLock = this.AcquireGVFSLock(mockTracer.Object);

            // Should be able to call again when GVFS has the lock
            gvfsLock.TryAcquireLockForGVFS().ShouldBeTrue();
            this.ValidateLockHeldByGVFS(gvfsLock);

            gvfsLock.ReleaseLockHeldByGVFS();
            this.ValidateLockIsFree(gvfsLock);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void TryAcquireLockForGVFS_WhenExternalLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", It.IsAny<EventMetadata>()));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);

            gvfsLock.TryAcquireLockForGVFS().ShouldBeFalse();
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenGVFSLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            GVFSLock gvfsLock = this.AcquireGVFSLock(mockTracer.Object);

            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(DefaultLockData, out existingExternalHolder).ShouldBeFalse();
            this.ValidateLockHeldByGVFS(gvfsLock);
            existingExternalHolder.ShouldBeNull();
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenExternalLock()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Verbose, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);

            NamedPipeMessages.LockData newLockData = new NamedPipeMessages.LockData(4321, false, false, "git new", "123");
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(newLockData, out existingExternalHolder).ShouldBeFalse();
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
            this.ValidateExistingExternalHolder(DefaultLockData, existingExternalHolder);
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
            mockTracer.VerifyAll();
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenExternalHolderTerminated()
        {
            Mock<ITracer> mockTracer = new Mock<ITracer>(MockBehavior.Strict);
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "TryAcquireLockExternal", It.IsAny<EventMetadata>()));
            mockTracer.Setup(x => x.RelatedEvent(EventLevel.Informational, "ExternalLockHolderExited", It.IsAny<EventMetadata>(), Keywords.Telemetry));
            mockTracer.Setup(x => x.SetGitCommandSessionId(string.Empty));
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform, mockTracer.Object);
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);

            NamedPipeMessages.LockData newLockData = new NamedPipeMessages.LockData(4321, false, false, "git new", "123");
            mockPlatform.ActiveProcesses.Add(newLockData.PID);
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(newLockData, out existingExternalHolder).ShouldBeTrue();
            existingExternalHolder.ShouldBeNull();
            this.ValidateLockHeld(gvfsLock, newLockData);
            mockTracer.VerifyAll();
        }

        private GVFSLock AcquireDefaultLock(MockPlatform mockPlatform, ITracer mockTracer)
        {
            GVFSLock gvfsLock = new GVFSLock(mockTracer);
            this.ValidateLockIsFree(gvfsLock);
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(DefaultLockData, out existingExternalHolder).ShouldBeTrue();
            existingExternalHolder.ShouldBeNull();
            mockPlatform.ActiveProcesses.Add(DefaultLockData.PID);
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
            return gvfsLock;
        }

        private GVFSLock AcquireGVFSLock(ITracer mockTracer)
        {
            GVFSLock gvfsLock = new GVFSLock(mockTracer);
            this.ValidateLockIsFree(gvfsLock);
            gvfsLock.TryAcquireLockForGVFS().ShouldBeTrue();
            this.ValidateLockHeldByGVFS(gvfsLock);
            return gvfsLock;
        }

        private void ValidateLockIsFree(GVFSLock gvfsLock)
        {
            this.ValidateLock(gvfsLock, null, expectedStatus: "Free", expectedGitCommand: null, expectedIsAvailable: true);
        }

        private void ValidateLockHeldByGVFS(GVFSLock gvfsLock)
        {
            this.ValidateLock(gvfsLock, null, expectedStatus: "Held by GVFS.", expectedGitCommand: null, expectedIsAvailable: false);
        }

        private void ValidateLockHeld(GVFSLock gvfsLock, NamedPipeMessages.LockData expected)
        {
            this.ValidateLock(gvfsLock, expected, expectedStatus: $"Held by {expected.ParsedCommand} (PID:{expected.PID})", expectedGitCommand: expected.ParsedCommand, expectedIsAvailable: false);
        }

        private void ValidateLock(
            GVFSLock gvfsLock,
            NamedPipeMessages.LockData expected,
            string expectedStatus,
            string expectedGitCommand,
            bool expectedIsAvailable)
        {
            gvfsLock.GetStatus().ShouldEqual(expectedStatus);
            NamedPipeMessages.LockData existingHolder;
            gvfsLock.IsLockAvailableForExternalRequestor(out existingHolder).ShouldEqual(expectedIsAvailable);
            this.ValidateExistingExternalHolder(expected, existingHolder);
            gvfsLock.GetLockedGitCommand().ShouldEqual(expectedGitCommand);
            NamedPipeMessages.LockData externalHolder = gvfsLock.GetExternalHolder();
            this.ValidateExistingExternalHolder(expected, externalHolder);
        }

        private void ValidateExistingExternalHolder(NamedPipeMessages.LockData expected, NamedPipeMessages.LockData actual)
        {
            if (actual != null)
            {
                expected.ShouldNotBeNull();
                actual.ShouldNotBeNull();
                actual.PID.ShouldEqual(expected.PID);
                actual.IsElevated.ShouldEqual(expected.IsElevated);
                actual.CheckAvailabilityOnly.ShouldEqual(expected.CheckAvailabilityOnly);
                actual.ParsedCommand.ShouldEqual(expected.ParsedCommand);
                actual.GitCommandSessionId.ShouldEqual(expected.GitCommandSessionId);
            }
            else
            {
                expected.ShouldBeNull();
            }
        }
    }
}
