using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Windows
{
    [TestFixture]
    public class WindowsFileSystemTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            this.tempDirectory = Path.Combine(Path.GetTempPath(), "GVFSWindowsFileSystemTests_" + Guid.NewGuid());
            Directory.CreateDirectory(this.tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.tempDirectory))
            {
                Directory.Delete(this.tempDirectory, recursive: true);
            }
        }

        [TestCase]
        public void HydrateFileSucceedsAndReportsNoFailure()
        {
            string filePath = Path.Combine(this.tempDirectory, "success.txt");
            File.WriteAllText(filePath, "contents");

            WindowsFileSystem fileSystem = new WindowsFileSystem();
            bool succeeded = fileSystem.HydrateFile(filePath, new byte[1], out Exception failure);

            succeeded.ShouldBeTrue();
            failure.ShouldBeNull();
        }

        [TestCase]
        public void HydrateFileCapturesIOExceptionOnSharingViolation()
        {
            string filePath = Path.Combine(this.tempDirectory, "locked.txt");
            File.WriteAllText(filePath, "contents");

            WindowsFileSystem fileSystem = new WindowsFileSystem();

            // Open the file exclusively so the FileStream opened inside HydrateFile
            // (FileShare.ReadWrite | FileShare.Delete) hits a real sharing violation.
            using (new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool succeeded = fileSystem.HydrateFile(filePath, new byte[1], out Exception failure);

                succeeded.ShouldBeFalse();
                failure.ShouldNotBeNull();
                failure.ShouldBeOfType<IOException>();

                // Pin the actual Win32-wrapped HResult (ERROR_SHARING_VIOLATION = 0x20) rather than
                // just asserting non-zero — a regression that wraps a different/unwrapped error would
                // otherwise pass this test while breaking HydrationFailureDiagnostics's derivation.
                failure.HResult.ShouldEqual(unchecked((int)0x80070020));
            }
        }

        [TestCase]
        public void HydrateFileCapturesUnauthorizedAccessExceptionOnDirectory()
        {
            // FileStream refuses to open a directory as a file with UnauthorizedAccessException.
            WindowsFileSystem fileSystem = new WindowsFileSystem();
            bool succeeded = fileSystem.HydrateFile(this.tempDirectory, new byte[1], out Exception failure);

            succeeded.ShouldBeFalse();
            failure.ShouldNotBeNull();
            failure.ShouldBeOfType<UnauthorizedAccessException>();

            // Pin the actual Win32-wrapped HResult (ERROR_ACCESS_DENIED = 0x5), same rationale as
            // the sharing-violation case above.
            failure.HResult.ShouldEqual(unchecked((int)0x80070005));
        }

        [TestCase]
        public void HydrateFileCapturesFileNotFoundExceptionOnMissingFile()
        {
            string filePath = Path.Combine(this.tempDirectory, "missing.txt");

            WindowsFileSystem fileSystem = new WindowsFileSystem();
            bool succeeded = fileSystem.HydrateFile(filePath, new byte[1], out Exception failure);

            succeeded.ShouldBeFalse();
            failure.ShouldNotBeNull();
            failure.ShouldBeOfType<FileNotFoundException>();

            // ERROR_FILE_NOT_FOUND = 0x2
            failure.HResult.ShouldEqual(unchecked((int)0x80070002));
        }
    }
}
