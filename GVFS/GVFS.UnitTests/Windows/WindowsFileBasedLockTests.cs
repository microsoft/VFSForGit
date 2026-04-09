using GVFS.Common;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Windows
{
    [TestFixture]
    public class WindowsFileBasedLockTests
    {
        [TestCase]
        public void CreateLockWhenDirectoryMissing()
        {
            string parentPath = Path.Combine("mock:", "path", "to");
            string lockPath = Path.Combine(parentPath, "lock");
            MockTracer tracer = new MockTracer();
            FileBasedLockFileSystem fs = new FileBasedLockFileSystem();
            FileBasedLock fileBasedLock = new WindowsFileBasedLock(fs, tracer, lockPath);

            fileBasedLock.TryAcquireLock().ShouldBeTrue();
            fs.CreateDirectoryPath.ShouldNotBeNull();
            fs.CreateDirectoryPath.ShouldEqual(parentPath);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void AttemptToAcquireLockWhenAlreadyLocked()
        {
            string parentPath = Path.Combine("mock:", "path", "to");
            string lockPath = Path.Combine(parentPath, "lock");
            MockTracer tracer = new MockTracer();
            FileBasedLockFileSystem fs = new FileBasedLockFileSystem();
            FileBasedLock fileBasedLock = new WindowsFileBasedLock(fs, tracer, lockPath);

            fileBasedLock.TryAcquireLock().ShouldBeTrue();
            Assert.Throws<InvalidOperationException>(() => fileBasedLock.TryAcquireLock());
        }

        private class FileBasedLockFileSystem : ConfigurableFileSystem
        {
            public string CreateDirectoryPath { get; set; }

            public override void CreateDirectory(string path)
            {
                this.CreateDirectoryPath = path;
            }

            public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
            {
                return new MemoryStream();
            }
        }
    }
}
