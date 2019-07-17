using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.EnlistmentPerFixture;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class BasicFileSystemTests : TestsWithEnlistmentPerFixture
    {
        private const int FileAttributeSparseFile = 0x00000200;
        private const int FileAttributeReparsePoint = 0x00000400;
        private const int FileAttributeRecallOnDataAccess = 0x00400000;

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void ShrinkFileContents(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "ShrinkFileContents");
            string originalVirtualContents = "0123456789";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(filename), originalVirtualContents);
            this.Enlistment.GetVirtualPathTo(filename).ShouldBeAFile(fileSystem).WithContents(originalVirtualContents);

            string newText = "112233";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(filename), newText);
            this.Enlistment.GetVirtualPathTo(filename).ShouldBeAFile(fileSystem).WithContents(newText);
            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(filename));

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void GrowFileContents(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "GrowFileContents");
            string originalVirtualContents = "112233";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(filename), originalVirtualContents);
            this.Enlistment.GetVirtualPathTo(filename).ShouldBeAFile(fileSystem).WithContents(originalVirtualContents);

            string newText = "0123456789";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(filename), newText);
            this.Enlistment.GetVirtualPathTo(filename).ShouldBeAFile(fileSystem).WithContents(newText);
            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(filename));

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void FilesAreBufferedAndCanBeFlushed(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "FilesAreBufferedAndCanBeFlushed");
            string filePath = this.Enlistment.GetVirtualPathTo(filename);

            byte[] buffer = System.Text.Encoding.ASCII.GetBytes("Some test data");

            using (FileStream writeStream = File.Open(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                writeStream.Write(buffer, 0, buffer.Length);

                using (FileStream readStream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    readStream.Length.ShouldEqual(0);
                    writeStream.Flush();
                    readStream.Length.ShouldEqual(buffer.Length);

                    byte[] readBuffer = new byte[buffer.Length];
                    readStream.Read(readBuffer, 0, readBuffer.Length).ShouldEqual(readBuffer.Length);
                    readBuffer.ShouldMatchInOrder(buffer);
                }
            }

            fileSystem.DeleteFile(filePath);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        [Category(Categories.WindowsOnly)]
        public void NewFileAttributesAreUpdated(string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "FileAttributesAreUpdated");
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string virtualFile = this.Enlistment.GetVirtualPathTo(filename);
            virtualFile.ShouldNotExistOnDisk(fileSystem);

            File.Create(virtualFile).Dispose();
            virtualFile.ShouldBeAFile(fileSystem);

            // Update defaults. FileInfo is not batched, so each of these will create a separate Open-Update-Close set.
            FileInfo before = new FileInfo(virtualFile);
            DateTime testValue = DateTime.Now + TimeSpan.FromDays(1);
            before.CreationTime = testValue;
            before.LastAccessTime = testValue;
            before.LastWriteTime = testValue;
            before.Attributes = FileAttributes.Hidden;

            // FileInfo caches information. We can refresh, but just to be absolutely sure...
            virtualFile.ShouldBeAFile(fileSystem).WithInfo(testValue, testValue, testValue, FileAttributes.Hidden);

            File.Delete(virtualFile);
            virtualFile.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        [Category(Categories.WindowsOnly)]
        public void NewFolderAttributesAreUpdated(string parentFolder)
        {
            string folderName = Path.Combine(parentFolder, "FolderAttributesAreUpdated");
            string virtualFolder = this.Enlistment.GetVirtualPathTo(folderName);
            Directory.CreateDirectory(virtualFolder);

            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            virtualFolder.ShouldBeADirectory(fileSystem);

            // Update defaults. DirectoryInfo is not batched, so each of these will create a separate Open-Update-Close set.
            DirectoryInfo before = new DirectoryInfo(virtualFolder);
            DateTime testValue = DateTime.Now + TimeSpan.FromDays(1);
            before.CreationTime = testValue;
            before.LastAccessTime = testValue;
            before.LastWriteTime = testValue;
            before.Attributes = FileAttributes.Hidden;

            // DirectoryInfo caches information. We can refresh, but just to be absolutely sure...
            virtualFolder.ShouldBeADirectory(fileSystem)
                .WithInfo(testValue, testValue, testValue, FileAttributes.Hidden | FileAttributes.Directory, ignoreRecallAttributes: false);

            Directory.Delete(virtualFolder);
        }

        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void ExpandedFileAttributesAreUpdated()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine("GVFS", "GVFS", "GVFS.csproj");
            string virtualFile = this.Enlistment.GetVirtualPathTo(filename);

            // Update defaults. FileInfo is not batched, so each of these will create a separate Open-Update-Close set.
            FileInfo before = new FileInfo(virtualFile);
            DateTime testValue = DateTime.Now + TimeSpan.FromDays(1);

            // Setting the CreationTime results in a write handle being open to the file and the file being expanded
            before.CreationTime = testValue;
            before.LastAccessTime = testValue;
            before.LastWriteTime = testValue;
            before.Attributes = FileAttributes.Hidden;

            // FileInfo caches information. We can refresh, but just to be absolutely sure...
            FileInfo info = virtualFile.ShouldBeAFile(fileSystem).WithInfo(testValue, testValue, testValue);

            // Ignore the archive bit as it can be re-added to the file as part of its expansion to full
            FileAttributes attributes = info.Attributes & ~FileAttributes.Archive;

            int retryCount = 0;
            int maxRetries = 10;
            while (attributes != FileAttributes.Hidden && retryCount < maxRetries)
            {
                // ProjFS attributes are remoted asynchronously when files are converted to full
                FileAttributes attributesLessProjFS = attributes & (FileAttributes)~(FileAttributeSparseFile | FileAttributeReparsePoint | FileAttributeRecallOnDataAccess);

                attributesLessProjFS.ShouldEqual(
                    FileAttributes.Hidden,
                    $"Attributes (ignoring ProjFS attributes) do not match, expected: {FileAttributes.Hidden} actual: {attributesLessProjFS}");

                ++retryCount;
                Thread.Sleep(500);

                info.Refresh();
                attributes = info.Attributes & ~FileAttributes.Archive;
            }

            attributes.ShouldEqual(FileAttributes.Hidden, $"Attributes do not match, expected: {FileAttributes.Hidden} actual: {attributes}");
        }

        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void UnhydratedFolderAttributesAreUpdated()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string folderName = Path.Combine("GVFS", "GVFS", "CommandLine");
            string virtualFolder = this.Enlistment.GetVirtualPathTo(folderName);

            // Update defaults. DirectoryInfo is not batched, so each of these will create a separate Open-Update-Close set.
            DirectoryInfo before = new DirectoryInfo(virtualFolder);
            DateTime testValue = DateTime.Now + TimeSpan.FromDays(1);
            before.CreationTime = testValue;
            before.LastAccessTime = testValue;
            before.LastWriteTime = testValue;
            before.Attributes = FileAttributes.Hidden;

            // DirectoryInfo caches information. We can refresh, but just to be absolutely sure...
            virtualFolder.ShouldBeADirectory(fileSystem)
                .WithInfo(testValue, testValue, testValue, FileAttributes.Hidden | FileAttributes.Directory, ignoreRecallAttributes: true);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void CannotWriteToReadOnlyFile(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "CannotWriteToReadOnlyFile");
            string virtualFilePath = this.Enlistment.GetVirtualPathTo(filename);
            virtualFilePath.ShouldNotExistOnDisk(fileSystem);

            // Write initial contents
            string originalContents = "Contents of ReadOnly file";
            fileSystem.WriteAllText(virtualFilePath, originalContents);
            virtualFilePath.ShouldBeAFile(fileSystem).WithContents(originalContents);

            // Make file read only
            FileInfo fileInfo = new FileInfo(virtualFilePath);
            fileInfo.Attributes = FileAttributes.ReadOnly;

            // Verify that file cannot be written to
            string newContents = "New contents for file";
            fileSystem.WriteAllTextShouldFail<UnauthorizedAccessException>(virtualFilePath, newContents);
            virtualFilePath.ShouldBeAFile(fileSystem).WithContents(originalContents);

            // Cleanup
            fileInfo.Attributes = FileAttributes.Normal;
            fileSystem.DeleteFile(virtualFilePath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void ReadonlyCanBeSetAndUnset(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "ReadonlyCanBeSetAndUnset");
            string virtualFilePath = this.Enlistment.GetVirtualPathTo(filename);
            virtualFilePath.ShouldNotExistOnDisk(fileSystem);

            string originalContents = "Contents of ReadOnly file";
            fileSystem.WriteAllText(virtualFilePath, originalContents);

            // Make file read only
            FileInfo fileInfo = new FileInfo(virtualFilePath);
            fileInfo.Attributes = FileAttributes.ReadOnly;
            virtualFilePath.ShouldBeAFile(fileSystem).WithAttribute(FileAttributes.ReadOnly);

            // Clear read only
            fileInfo.Attributes = FileAttributes.Normal;
            virtualFilePath.ShouldBeAFile(fileSystem).WithoutAttribute(FileAttributes.ReadOnly);

            // Cleanup
            fileSystem.DeleteFile(virtualFilePath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void ChangeVirtualNTFSFileNameCase(FileSystemRunner fileSystem, string parentFolder)
        {
            string oldFilename = Path.Combine(parentFolder, "ChangePhysicalFileNameCase.txt");
            string newFilename = Path.Combine(parentFolder, "changephysicalfilenamecase.txt");
            string fileContents = "Hello World";
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, oldFilename, parentFolder);

            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(oldFilename), fileContents);
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(fileSystem).WithContents(fileContents);
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(fileSystem).WithCaseMatchingName(Path.GetFileName(oldFilename));

            fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldFilename), this.Enlistment.GetVirtualPathTo(newFilename));
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldBeAFile(fileSystem).WithContents(fileContents);
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldBeAFile(fileSystem).WithCaseMatchingName(Path.GetFileName(newFilename));

            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(newFilename));

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, newFilename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void ChangeVirtualNTFSFileName(FileSystemRunner fileSystem, string parentFolder)
        {
            string oldFilename = Path.Combine(parentFolder, "ChangePhysicalFileName.txt");
            string newFilename = Path.Combine(parentFolder, "NewFileName.txt");
            string fileContents = "Hello World";
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, oldFilename, parentFolder);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, newFilename, parentFolder);

            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(oldFilename), fileContents);
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(fileSystem).WithContents(fileContents);
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldNotExistOnDisk(fileSystem);

            fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldFilename), this.Enlistment.GetVirtualPathTo(newFilename));
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldBeAFile(fileSystem).WithContents(fileContents);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, oldFilename, parentFolder);

            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(newFilename));
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, newFilename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void MoveVirtualNTFSFileToVirtualNTFSFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);

            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldBeADirectory(fileSystem);

            string testFileName = Path.Combine(parentFolder, "test.txt");
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(testFileName), testFileContents);
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string newTestFileVirtualPath = Path.Combine(
                this.Enlistment.GetVirtualPathTo(testFolderName),
                Path.GetFileName(testFileName));

            fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(testFileName), newTestFileVirtualPath);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFileName, parentFolder);
            newTestFileVirtualPath.ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.DeleteFile(newTestFileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, Path.Combine(testFolderName, Path.GetFileName(testFileName)), parentFolder);

            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void MoveWorkingDirectoryFileToDotGitFolder(FileSystemRunner fileSystem)
        {
            string testFolderName = ".git";
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldBeADirectory(fileSystem);

            string testFileName = "test.txt";
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldNotExistOnDisk(fileSystem);

            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(testFileName), testFileContents);
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string newTestFileVirtualPath = Path.Combine(this.Enlistment.GetVirtualPathTo(testFolderName), testFileName);
            fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(testFileName), newTestFileVirtualPath);
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldNotExistOnDisk(fileSystem);
            newTestFileVirtualPath.ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.DeleteFile(newTestFileVirtualPath);
            newTestFileVirtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void MoveDotGitFileToWorkingDirectoryFolder(FileSystemRunner fileSystem)
        {
            string testFolderName = "test_folder";
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldBeADirectory(fileSystem);

            string sourceFileFolder = ".git";
            string testFileName = "config";
            string sourceFileVirtualPath = Path.Combine(this.Enlistment.GetVirtualPathTo(sourceFileFolder), testFileName);
            string testFileContents = sourceFileVirtualPath.ShouldBeAFile(fileSystem).WithContents();

            string targetTestFileVirtualPath = Path.Combine(this.Enlistment.GetVirtualPathTo(testFolderName), testFileName);

            fileSystem.MoveFile(sourceFileVirtualPath, targetTestFileVirtualPath);
            sourceFileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            targetTestFileVirtualPath.ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.MoveFile(targetTestFileVirtualPath, sourceFileVirtualPath);
            sourceFileVirtualPath.ShouldBeAFile(fileSystem).WithContents(testFileContents);
            targetTestFileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void MoveVirtualNTFSFileToOverwriteVirtualNTFSFile(FileSystemRunner fileSystem, string parentFolder)
        {
            string targetFilename = Path.Combine(parentFolder, "TargetFile.txt");
            string sourceFilename = Path.Combine(parentFolder, "SourceFile.txt");
            string targetFileContents = "The Target";
            string sourceFileContents = "The Source";
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFilename, parentFolder);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, sourceFilename, parentFolder);

            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(targetFilename), targetFileContents);
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(fileSystem).WithContents(targetFileContents);

            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(sourceFilename), sourceFileContents);
            this.Enlistment.GetVirtualPathTo(sourceFilename).ShouldBeAFile(fileSystem).WithContents(sourceFileContents);

            fileSystem.ReplaceFile(this.Enlistment.GetVirtualPathTo(sourceFilename), this.Enlistment.GetVirtualPathTo(targetFilename));

            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(fileSystem).WithContents(sourceFileContents);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, sourceFilename, parentFolder);

            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(targetFilename));

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFilename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void MoveVirtualNTFSFileToInvalidFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);

            string testFileName = Path.Combine(parentFolder, "test.txt");
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(testFileName), testFileContents);
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string newTestFileVirtualPath = Path.Combine(
                this.Enlistment.GetVirtualPathTo(testFolderName),
                Path.GetFileName(testFileName));

            fileSystem.MoveFileShouldFail(this.Enlistment.GetVirtualPathTo(testFileName), newTestFileVirtualPath);
            newTestFileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            this.Enlistment.GetVirtualPathTo(testFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(testFileName));
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFileName, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void DeletedFilesCanBeImmediatelyRecreated(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "DeletedFilesCanBeImmediatelyRecreated");
            string filePath = this.Enlistment.GetVirtualPathTo(filename);
            filePath.ShouldNotExistOnDisk(fileSystem);

            string testData = "Some test data";

            fileSystem.WriteAllText(filePath, testData);

            fileSystem.DeleteFile(filePath);

            // Do not check for delete. Doing so removes a race between deleting and writing.
            // This write will throw if the problem exists.
            fileSystem.WriteAllText(filePath, testData);

            filePath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(testData);
            fileSystem.DeleteFile(filePath);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.CanDeleteFilesWhileTheyAreOpenRunners))]
        [Category(Categories.LinuxTODO.NeedsContentionFreeFileLock)]
        public void CanDeleteFilesWhileTheyAreOpen(FileSystemRunner fileSystem, string parentFolder)
        {
            string filename = Path.Combine(parentFolder, "CanDeleteFilesWhileTheyAreOpen");
            string filePath = this.Enlistment.GetVirtualPathTo(filename);

            byte[] buffer = System.Text.Encoding.ASCII.GetBytes("Some test data for writing");

            using (FileStream deletableWriteStream = File.Open(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                deletableWriteStream.Write(buffer, 0, buffer.Length);
                deletableWriteStream.Flush();

                using (FileStream deletableReadStream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] readBuffer = new byte[buffer.Length];

                    deletableReadStream.Read(readBuffer, 0, readBuffer.Length).ShouldEqual(readBuffer.Length);
                    readBuffer.ShouldMatchInOrder(buffer);

                    fileSystem.DeleteFile(filePath);
                    this.VerifyExistenceAfterDeleteWhileOpen(filePath, fileSystem);

                    deletableWriteStream.Write(buffer, 0, buffer.Length);
                    deletableWriteStream.Flush();
                }
            }

            filePath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCase]
        [Category(Categories.LinuxTODO.NeedsContentionFreeFileLock)]
        public void CanDeleteHydratedFilesWhileTheyAreOpenForWrite()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string fileName = "GVFS.sln";
            string virtualPath = this.Enlistment.GetVirtualPathTo(fileName);

            virtualPath.ShouldBeAFile(fileSystem);

            using (Stream stream = new FileStream(virtualPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream))
            {
                // First line is empty, so read two lines
                string line = reader.ReadLine() + reader.ReadLine();
                line.Length.ShouldNotEqual(0);

                File.Delete(virtualPath);
                this.VerifyExistenceAfterDeleteWhileOpen(virtualPath, fileSystem);

                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.WriteLine("newline!");
                    writer.Flush();
                    this.VerifyExistenceAfterDeleteWhileOpen(virtualPath, fileSystem);
                }
            }

            virtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        // WindowsOnly because file timestamps on Mac are set to the time at which
        // placeholders are written
        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void ProjectedBlobFileTimesMatchHead()
        {
            // TODO: 467539 - Update all runners to support getting create/modify/access times
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = "AuthoringTests.md";
            string headFileName = Path.Combine(".git", "logs", "HEAD");
            this.Enlistment.GetVirtualPathTo(headFileName).ShouldBeAFile(fileSystem);

            FileInfo headFileInfo = new FileInfo(this.Enlistment.GetVirtualPathTo(headFileName));
            FileInfo fileInfo = new FileInfo(this.Enlistment.GetVirtualPathTo(filename));

            fileInfo.CreationTime.ShouldEqual(headFileInfo.CreationTime);

            // Last access and last write can get set outside the test, make sure that are at least
            // as recent as the creation time on the HEAD file, and no later than now
            fileInfo.LastAccessTime.ShouldBeAtLeast(headFileInfo.CreationTime);
            fileInfo.LastWriteTime.ShouldBeAtLeast(headFileInfo.CreationTime);
            fileInfo.LastAccessTime.ShouldBeAtMost(DateTime.Now);
            fileInfo.LastWriteTime.ShouldBeAtMost(DateTime.Now);
        }

        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void ProjectedBlobFolderTimesMatchHead()
        {
            // TODO: 467539 - Update all runners to support getting create/modify/access times
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string folderName = Path.Combine("GVFS", "GVFS.Tests");
            string headFileName = Path.Combine(".git", "logs", "HEAD");
            this.Enlistment.GetVirtualPathTo(headFileName).ShouldBeAFile(fileSystem);

            FileInfo headFileInfo = new FileInfo(this.Enlistment.GetVirtualPathTo(headFileName));
            DirectoryInfo folderInfo = new DirectoryInfo(this.Enlistment.GetVirtualPathTo(folderName));

            folderInfo.CreationTime.ShouldEqual(headFileInfo.CreationTime);

            // Last access and last write can get set outside the test, make sure that are at least
            // as recent as the creation time on the HEAD file, and no later than now
            folderInfo.LastAccessTime.ShouldBeAtLeast(headFileInfo.CreationTime);
            folderInfo.LastWriteTime.ShouldBeAtLeast(headFileInfo.CreationTime);
            folderInfo.LastAccessTime.ShouldBeAtMost(DateTime.Now);
            folderInfo.LastWriteTime.ShouldBeAtMost(DateTime.Now);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void NonExistentItemBehaviorIsCorrect(FileSystemRunner fileSystem, string parentFolder)
        {
            string nonExistentItem = Path.Combine(parentFolder, "BadFolderName");
            string nonExistentItem2 = Path.Combine(parentFolder, "BadFolderName2");

            string virtualPathToNonExistentItem = this.Enlistment.GetVirtualPathTo(nonExistentItem).ShouldNotExistOnDisk(fileSystem);
            string virtualPathToNonExistentItem2 = this.Enlistment.GetVirtualPathTo(nonExistentItem2).ShouldNotExistOnDisk(fileSystem);

            fileSystem.MoveFile_FileShouldNotBeFound(virtualPathToNonExistentItem, virtualPathToNonExistentItem2);
            fileSystem.DeleteFile_FileShouldNotBeFound(virtualPathToNonExistentItem);
            fileSystem.ReplaceFile_FileShouldNotBeFound(virtualPathToNonExistentItem, virtualPathToNonExistentItem2);
            fileSystem.ReadAllText_FileShouldNotBeFound(virtualPathToNonExistentItem);

            // TODO #457434
            // fileSystem.MoveDirectoryShouldNotBeFound(nonExistentItem, true)
            fileSystem.DeleteDirectory_DirectoryShouldNotBeFound(virtualPathToNonExistentItem);

            // TODO #457434
            // fileSystem.ReplaceDirectoryShouldNotBeFound(nonExistentItem, true)
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void RenameEmptyVirtualNTFSFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            testFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newFolderName = Path.Combine(parentFolder, "test_folder_renamed");
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(newFolderName);
            newFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.MoveDirectory(testFolderVirtualPath, newFolderVirtualPath);
            testFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);
            newFolderVirtualPath.ShouldBeADirectory(fileSystem);

            fileSystem.DeleteDirectory(newFolderVirtualPath);
            newFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void MoveVirtualNTFSFolderIntoVirtualNTFSFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string targetFolderName = Path.Combine(parentFolder, "target_folder");
            string targetFolderVirtualPath = this.Enlistment.GetVirtualPathTo(targetFolderName);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFolderName, parentFolder);

            fileSystem.CreateDirectory(targetFolderVirtualPath);
            targetFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string testFileName = Path.Combine(testFolderName, "test.txt");
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(testFileName), testFileContents);
            this.Enlistment.GetVirtualPathTo(testFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string newTestFolder = Path.Combine(targetFolderName, Path.GetFileName(testFolderName));
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(newTestFolder);

            fileSystem.MoveDirectory(testFolderVirtualPath, newFolderVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);
            newFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newTestFileName = Path.Combine(newTestFolder, Path.GetFileName(testFileName));
            this.Enlistment.GetVirtualPathTo(newTestFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.DeleteDirectory(targetFolderVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFolderName, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void RenameAndMoveVirtualNTFSFolderIntoVirtualNTFSFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string targetFolderName = Path.Combine(parentFolder, "target_folder");
            string targetFolderVirtualPath = this.Enlistment.GetVirtualPathTo(targetFolderName);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFolderName, parentFolder);

            fileSystem.CreateDirectory(targetFolderVirtualPath);
            targetFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string testFileName = "test.txt";
            string testFilePartialPath = Path.Combine(testFolderName, testFileName);
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(testFilePartialPath), testFileContents);
            this.Enlistment.GetVirtualPathTo(testFilePartialPath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string newTestFolder = Path.Combine(targetFolderName, "test_folder_renamed");
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(newTestFolder);

            fileSystem.MoveDirectory(testFolderVirtualPath, newFolderVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderName, parentFolder);
            newFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newTestFileName = Path.Combine(newTestFolder, testFileName);
            this.Enlistment.GetVirtualPathTo(newTestFileName).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            fileSystem.DeleteDirectory(targetFolderVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, targetFolderName, parentFolder);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void MoveVirtualNTFSFolderTreeIntoVirtualNTFSFolder(FileSystemRunner fileSystem)
        {
            string testFolderParent = "test_folder_parent";
            string testFolderChild = "test_folder_child";
            string testFolderGrandChild = "test_folder_grandchild";
            string testFile = "test.txt";
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);

            // Create the folder tree (to move)
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(testFolderParent));
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldBeADirectory(fileSystem);

            string realtiveChildFolderPath = Path.Combine(testFolderParent, testFolderChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);

            string realtiveGrandChildFolderPath = Path.Combine(realtiveChildFolderPath, testFolderGrandChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);

            string relativeTestFilePath = Path.Combine(realtiveGrandChildFolderPath, testFile);
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(relativeTestFilePath), testFileContents);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Create the target
            string targetFolder = "target_folder";
            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(targetFolder));
            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldBeADirectory(fileSystem);

            fileSystem.MoveDirectory(
                this.Enlistment.GetVirtualPathTo(testFolderParent),
                this.Enlistment.GetVirtualPathTo(Path.Combine(targetFolder, testFolderParent)));

            // The old tree structure should be gone
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldNotExistOnDisk(fileSystem);

            // The tree should have been moved under the target folder
            testFolderParent = Path.Combine(targetFolder, testFolderParent);
            realtiveChildFolderPath = Path.Combine(testFolderParent, testFolderChild);
            realtiveGrandChildFolderPath = Path.Combine(realtiveChildFolderPath, testFolderGrandChild);
            relativeTestFilePath = Path.Combine(realtiveGrandChildFolderPath, testFile);

            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Cleanup
            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(targetFolder));

            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void MoveDotGitFullFolderTreeToDotGitFullFolder(FileSystemRunner fileSystem)
        {
            string testFolderRoot = ".git";
            string testFolderParent = "test_folder_parent";
            string testFolderChild = "test_folder_child";
            string testFolderGrandChild = "test_folder_grandchild";
            string testFile = "test.txt";
            this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderRoot, testFolderParent)).ShouldNotExistOnDisk(fileSystem);

            // Create the folder tree (to move)
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderRoot, testFolderParent)));
            this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderRoot, testFolderParent)).ShouldBeADirectory(fileSystem);

            string realtiveChildFolderPath = Path.Combine(testFolderRoot, testFolderParent, testFolderChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);

            string realtiveGrandChildFolderPath = Path.Combine(realtiveChildFolderPath, testFolderGrandChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);

            string relativeTestFilePath = Path.Combine(realtiveGrandChildFolderPath, testFile);
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(relativeTestFilePath), testFileContents);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Create the target
            string targetFolder = Path.Combine(".git", "target_folder");
            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(targetFolder));
            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldBeADirectory(fileSystem);

            fileSystem.MoveDirectory(
                this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderRoot, testFolderParent)),
                this.Enlistment.GetVirtualPathTo(Path.Combine(targetFolder, testFolderParent)));

            // The old tree structure should be gone
            this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderRoot, testFolderParent)).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldNotExistOnDisk(fileSystem);

            // The tree should have been moved under the target folder
            testFolderParent = Path.Combine(targetFolder, testFolderParent);
            realtiveChildFolderPath = Path.Combine(testFolderParent, testFolderChild);
            realtiveGrandChildFolderPath = Path.Combine(realtiveChildFolderPath, testFolderGrandChild);
            relativeTestFilePath = Path.Combine(realtiveGrandChildFolderPath, testFile);

            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Cleanup
            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(targetFolder));

            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void DeleteIndexFileFails(FileSystemRunner fileSystem)
        {
            string indexFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(".git", "index"));
            indexFilePath.ShouldBeAFile(fileSystem);
            fileSystem.DeleteFile_AccessShouldBeDenied(indexFilePath);
            indexFilePath.ShouldBeAFile(fileSystem);
        }

        // On some platforms, a pre-rename event may be delivered prior to a
        // file rename rather than a pre-delete event, so we check this
        // separately from the DeleteIndexFileFails() test case
        // This test is failing on Windows because the CmdRunner succeeds in moving the index file
        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        [Category(Categories.POSIXOnly)]
        public void MoveIndexFileFails(FileSystemRunner fileSystem)
        {
            string indexFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(".git", "index"));
            string indexTargetFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(".git", "index_target"));
            indexFilePath.ShouldBeAFile(fileSystem);
            indexTargetFilePath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.ReplaceFile_AccessShouldBeDenied(indexFilePath, indexTargetFilePath);
            indexFilePath.ShouldBeAFile(fileSystem);
            indexTargetFilePath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void MoveVirtualNTFSFolderIntoInvalidFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderParent = Path.Combine(parentFolder, "test_folder_parent");
            string testFolderChild = "test_folder_child";
            string testFolderGrandChild = "test_folder_grandchild";
            string testFile = "test.txt";
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderParent, parentFolder);

            // Create the folder tree (to move)
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(testFolderParent));
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldBeADirectory(fileSystem);

            string realtiveChildFolderPath = Path.Combine(testFolderParent, testFolderChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);

            string realtiveGrandChildFolderPath = Path.Combine(realtiveChildFolderPath, testFolderGrandChild);
            fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath));
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);

            string relativeTestFilePath = Path.Combine(realtiveGrandChildFolderPath, testFile);
            string testFileContents = "This is the contents of a test file";
            fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(relativeTestFilePath), testFileContents);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            string targetFolder = Path.Combine(parentFolder, "target_folder_does_not_exists");
            this.Enlistment.GetVirtualPathTo(targetFolder).ShouldNotExistOnDisk(fileSystem);

            // This move should fail
            fileSystem.MoveDirectory_TargetShouldBeInvalid(
                this.Enlistment.GetVirtualPathTo(testFolderParent),
                this.Enlistment.GetVirtualPathTo(Path.Combine(targetFolder, Path.GetFileName(testFolderParent))));

            // The old tree structure should still be there
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Cleanup
            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(testFolderParent));
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, testFolderParent, parentFolder);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, realtiveChildFolderPath, parentFolder);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, realtiveGrandChildFolderPath, parentFolder);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, relativeTestFilePath, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        [Category(Categories.WindowsOnly)]
        public void CreateFileInheritsParentDirectoryAttributes(string parentFolder)
        {
            string parentDirectoryPath = this.Enlistment.GetVirtualPathTo(Path.Combine(parentFolder, "CreateFileInheritsParentDirectoryAttributes"));
            FileSystemRunner.DefaultRunner.CreateDirectory(parentDirectoryPath);
            DirectoryInfo parentDirInfo = new DirectoryInfo(parentDirectoryPath);
            parentDirInfo.Attributes |= FileAttributes.NoScrubData;
            parentDirInfo.Attributes.HasFlag(FileAttributes.NoScrubData).ShouldEqual(true);

            string targetFilePath = Path.Combine(parentDirectoryPath, "TargetFile");
            FileSystemRunner.DefaultRunner.WriteAllText(targetFilePath, "Some contents that don't matter");
            targetFilePath.ShouldBeAFile(FileSystemRunner.DefaultRunner).WithAttribute(FileAttributes.NoScrubData);

            FileSystemRunner.DefaultRunner.DeleteDirectory(parentDirectoryPath);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        [Category(Categories.WindowsOnly)]
        public void CreateDirectoryInheritsParentDirectoryAttributes(string parentFolder)
        {
            string parentDirectoryPath = this.Enlistment.GetVirtualPathTo(Path.Combine(parentFolder, "CreateDirectoryInheritsParentDirectoryAttributes"));
            FileSystemRunner.DefaultRunner.CreateDirectory(parentDirectoryPath);
            DirectoryInfo parentDirInfo = new DirectoryInfo(parentDirectoryPath);
            parentDirInfo.Attributes |= FileAttributes.NoScrubData;
            parentDirInfo.Attributes.HasFlag(FileAttributes.NoScrubData).ShouldEqual(true);

            string targetDirPath = Path.Combine(parentDirectoryPath, "TargetDir");
            FileSystemRunner.DefaultRunner.CreateDirectory(targetDirPath);
            targetDirPath.ShouldBeADirectory(FileSystemRunner.DefaultRunner).WithAttribute(FileAttributes.NoScrubData);

            FileSystemRunner.DefaultRunner.DeleteDirectory(parentDirectoryPath);
        }

        [TestCase]
        [Category(Categories.POSIXOnly)]
        public void RunPythonExecutable()
        {
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout FunctionalTests/PythonExecutable");

            // Found an issue on Mac where running a python executable that is a placeholder, fails
            // The fix was to always hydrate executables (no placeholders for this mode)
            // To repro this issue in the C# framework the python executable must be run via a wrapper
            string pythonDirectory = Path.Combine(this.Enlistment.RepoRoot, "Test_Executable");
            string pythonExecutable = Path.Combine(pythonDirectory, "python_wrapper.sh");

            ProcessStartInfo startInfo = new ProcessStartInfo(pythonExecutable);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.WorkingDirectory = pythonDirectory;

            ProcessResult result = ProcessHelper.Run(startInfo);
            result.ExitCode.ShouldEqual(0);
            result.Output.ShouldContain("3.14");

            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout " + this.Enlistment.Commitish);
        }

        private void VerifyExistenceAfterDeleteWhileOpen(string filePath, FileSystemRunner fileSystem)
        {
            if (this.SupportsPosixDelete())
            {
                filePath.ShouldNotExistOnDisk(fileSystem);
            }
            else
            {
                filePath.ShouldBeAFile(fileSystem);
            }
        }

        private bool SupportsPosixDelete()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // https://msdn.microsoft.com/en-us/library/windows/desktop/ms724429(v=vs.85).aspx
                FileVersionInfo kernel32Info = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));

                // 18362 is first build with posix delete as the default in windows
                if (kernel32Info.FileBuildPart >= 18362)
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        private class FileRunnersAndFolders
        {
            private const string DotGitFolder = ".git";

            private static object[] allFolders =
            {
                new object[] { string.Empty },
                new object[] { DotGitFolder },
            };

            public static object[] Runners
            {
                get
                {
                    List<object[]> runnersAndParentFolders = new List<object[]>();
                    foreach (object[] runner in FileSystemRunner.Runners.ToList())
                    {
                        runnersAndParentFolders.Add(new object[] { runner.ToList().First(), string.Empty });
                        runnersAndParentFolders.Add(new object[] { runner.ToList().First(), DotGitFolder });
                    }

                    return runnersAndParentFolders.ToArray();
                }
            }

            public static object[] CanDeleteFilesWhileTheyAreOpenRunners
            {
                get
                {
                    // Don't use the BashRunner for the CanDeleteFilesWhileTheyAreOpen test as bash.exe (rm command) moves
                    // the file to the recycle bin rather than deleting it if the file that is getting removed is currently open.
                    List<object[]> runnersAndParentFolders = new List<object[]>();
                    foreach (object[] runner in FileSystemRunner.Runners.ToList())
                    {
                        if (!(runner.ToList().First() is BashRunner))
                        {
                            runnersAndParentFolders.Add(new object[] { runner.ToList().First(), string.Empty });
                            runnersAndParentFolders.Add(new object[] { runner.ToList().First(), DotGitFolder });
                        }
                    }

                    return runnersAndParentFolders.ToArray();
                }
            }

            public static object[] Folders
            {
                get
                {
                    return allFolders;
                }
            }

            public static void ShouldNotExistOnDisk(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem, string filename, string parentFolder)
            {
                enlistment.GetVirtualPathTo(filename).ShouldNotExistOnDisk(fileSystem);
            }
        }
    }
}
