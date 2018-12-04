using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Virtual;
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
            parsedCommand: "git command");

        [TestCase]
        public void TryAcquireAndReleaseLockForExternalRequestor()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);

            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID);
            this.ValidateLockIsFree(gvfsLock);
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_WhenNoLock()
        {
            GVFSLock gvfsLock = new GVFSLock(new MockTracer());
            this.ValidateLockIsFree(gvfsLock);
            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID).ShouldBeFalse();
            this.ValidateLockIsFree(gvfsLock);
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_DifferentPID()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);
            gvfsLock.ReleaseLockHeldByExternalProcess(4321).ShouldBeFalse();
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
        }

        [TestCase]
        public void ReleaseLockHeldByExternalProcess_WhenGVFSHasLock()
        {
            GVFSLock gvfsLock = this.AcquireGVFSLock();

            gvfsLock.ReleaseLockHeldByExternalProcess(DefaultLockData.PID).ShouldBeFalse();
            this.ValidateLockHeldByGVFS(gvfsLock);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReleaseLockHeldByGVFS_WhenNoLock()
        {
            GVFSLock gvfsLock = new GVFSLock(new MockTracer());
            this.ValidateLockIsFree(gvfsLock);
            Assert.Throws<InvalidOperationException>(() => gvfsLock.ReleaseLockHeldByGVFS());
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReleaseLockHeldByGVFS_WhenExternalHasLockShouldThrow()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);

            Assert.Throws<InvalidOperationException>(() => gvfsLock.ReleaseLockHeldByGVFS());
        }

        [TestCase]
        public void TryAcquireLockForGVFS()
        {
            GVFSLock gvfsLock = this.AcquireGVFSLock();

            // Should be able to call again when GVFS has the lock
            gvfsLock.TryAcquireLockForGVFS().ShouldBeTrue();
            this.ValidateLockHeldByGVFS(gvfsLock);

            gvfsLock.ReleaseLockHeldByGVFS();
            this.ValidateLockIsFree(gvfsLock);
        }

        [TestCase]
        public void TryAcquireLockForGVFS_WhenExternalLock()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);

            gvfsLock.TryAcquireLockForGVFS().ShouldBeFalse();
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenGVFSLock()
        {
            GVFSLock gvfsLock = this.AcquireGVFSLock();

            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(DefaultLockData, out existingExternalHolder).ShouldBeFalse();
            this.ValidateLockHeldByGVFS(gvfsLock);
            existingExternalHolder.ShouldBeNull();
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenExternalLock()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);

            NamedPipeMessages.LockData newLockData = new NamedPipeMessages.LockData(4321, false, false, "git new");
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(newLockData, out existingExternalHolder).ShouldBeFalse();
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
            this.ValidateExistingExternalHolder(DefaultLockData, existingExternalHolder);
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);
        }

        [TestCase]
        public void TryAcquireLockForExternalRequestor_WhenExternalHolderTerminated()
        {
            MockPlatform mockPlatform = (MockPlatform)GVFSPlatform.Instance;
            GVFSLock gvfsLock = this.AcquireDefaultLock(mockPlatform);
            mockPlatform.ActiveProcesses.Remove(DefaultLockData.PID);

            NamedPipeMessages.LockData newLockData = new NamedPipeMessages.LockData(4321, false, false, "git new");
            mockPlatform.ActiveProcesses.Add(newLockData.PID);
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(newLockData, out existingExternalHolder).ShouldBeTrue();
            existingExternalHolder.ShouldBeNull();
            this.ValidateLockHeld(gvfsLock, newLockData);
        }

        private GVFSLock AcquireDefaultLock(MockPlatform mockPlatform)
        {
            GVFSLock gvfsLock = new GVFSLock(new MockTracer());
            this.ValidateLockIsFree(gvfsLock);
            NamedPipeMessages.LockData existingExternalHolder;
            gvfsLock.TryAcquireLockForExternalRequestor(DefaultLockData, out existingExternalHolder).ShouldBeTrue();
            existingExternalHolder.ShouldBeNull();
            mockPlatform.ActiveProcesses.Add(DefaultLockData.PID);
            this.ValidateLockHeld(gvfsLock, DefaultLockData);
            return gvfsLock;
        }

        private GVFSLock AcquireGVFSLock()
        {
            GVFSLock gvfsLock = new GVFSLock(new MockTracer());
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
            }
            else
            {
                expected.ShouldBeNull();
            }
        }
    }
}
