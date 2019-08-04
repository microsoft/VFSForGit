using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
    public class WorkingDirectoryTests : TestsWithEnlistmentPerFixture
    {
        public const string TestFileContents =
@"// dllmain.cpp : Defines the entry point for the DLL application.
#include ""stdafx.h""

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
{
    UNREFERENCED_PARAMETER(hModule);
    UNREFERENCED_PARAMETER(lpReserved);

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

";
        private const int CurrentPlaceholderVersion = 1;

        private FileSystemRunner fileSystem;

        public WorkingDirectoryTests(FileSystemRunner fileSystem)
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void ProjectedFileHasExpectedContents()
        {
            this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "ProjectedFileHasExpectedContents.cpp")
                .ShouldBeAFile(this.fileSystem)
                .WithContents(TestFileContents);
        }

        [TestCase, Order(2)]
        public void StreamAccessReadWriteMemoryMappedProjectedFile()
        {
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "StreamAccessReadWriteMemoryMappedProjectedFile.cs");
            string contents = fileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents();
            StringBuilder contentsBuilder = new StringBuilder(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                // Length of the Byte-order-mark that will be at the start of the memory mapped file.
                // See https://msdn.microsoft.com/en-us/library/windows/desktop/dd374101(v=vs.85).aspx
                int bomOffset = 3;

                // offset -> Number of bytes from the start of the file where the view starts
                int offset = 64;
                int size = contents.Length;
                string newContent = "**NEWCONTENT**";

                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset, size - offset + bomOffset))
                {
                    streamAccessor.CanRead.ShouldEqual(true);
                    streamAccessor.CanWrite.ShouldEqual(true);

                    for (int i = offset; i < size - offset; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contents[i - bomOffset]);
                    }

                    // Reset to the start of the stream (which will place the streamAccessor at offset in the memory file)
                    streamAccessor.Seek(0, SeekOrigin.Begin);
                    byte[] newContentBuffer = Encoding.ASCII.GetBytes(newContent);

                    streamAccessor.Write(newContentBuffer, 0, newContent.Length);

                    for (int i = 0; i < newContent.Length; ++i)
                    {
                        contentsBuilder[offset + i - bomOffset] = newContent[i];
                    }

                    contents = contentsBuilder.ToString();
                }

                // Verify the file has the new contents inserted into it
                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset: 0, size: size + bomOffset))
                {
                    // Skip the BOM
                    for (int i = 0; i < bomOffset; ++i)
                    {
                        streamAccessor.ReadByte();
                    }

                    for (int i = 0; i < size; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contents[i]);
                    }
                }
            }

            // Confirm the new contents was written to disk
            fileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(contents);
        }

        [TestCase, Order(3)]
        public void RandomAccessReadWriteMemoryMappedProjectedFile()
        {
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "RandomAccessReadWriteMemoryMappedProjectedFile.cs");

            string contents = fileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents();
            StringBuilder contentsBuilder = new StringBuilder(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                // Length of the Byte-order-mark that will be at the start of the memory mapped file.
                // See https://msdn.microsoft.com/en-us/library/windows/desktop/dd374101(v=vs.85).aspx
                int bomOffset = 3;

                // offset -> Number of bytes from the start of the file where the view starts
                int offset = 64;
                int size = contents.Length;
                string newContent = "**NEWCONTENT**";

                using (MemoryMappedViewAccessor randomAccessor = mmf.CreateViewAccessor(offset, size - offset + bomOffset))
                {
                    randomAccessor.CanRead.ShouldEqual(true);
                    randomAccessor.CanWrite.ShouldEqual(true);

                    for (int i = 0; i < size - offset; ++i)
                    {
                        ((char)randomAccessor.ReadByte(i)).ShouldEqual(contents[i + offset - bomOffset]);
                    }

                    for (int i = 0; i < newContent.Length; ++i)
                    {
                        // Convert to byte before writing rather than writing as char, because char version will write a 16-bit
                        // unicode char
                        randomAccessor.Write(i, Convert.ToByte(newContent[i]));
                        ((char)randomAccessor.ReadByte(i)).ShouldEqual(newContent[i]);
                    }

                    for (int i = 0; i < newContent.Length; ++i)
                    {
                        contentsBuilder[offset + i - bomOffset] = newContent[i];
                    }

                    contents = contentsBuilder.ToString();
                }

                // Verify the file has the new contents inserted into it
                using (MemoryMappedViewAccessor randomAccessor = mmf.CreateViewAccessor(offset: 0, size: size + bomOffset))
                {
                    for (int i = 0; i < size; ++i)
                    {
                        ((char)randomAccessor.ReadByte(i + bomOffset)).ShouldEqual(contents[i]);
                    }
                }
            }

            // Confirm the new contents was written to disk
            fileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(contents);
        }

        [TestCase, Order(4)]
        public void StreamAndRandomAccessReadWriteMemoryMappedProjectedFile()
        {
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "StreamAndRandomAccessReadWriteMemoryMappedProjectedFile.cs");

            StringBuilder contentsBuilder = new StringBuilder();

            // Length of the Byte-order-mark that will be at the start of the memory mapped file.
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/dd374101(v=vs.85).aspx
            int bomOffset = 3;

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                // The text length of StreamAndRandomAccessReadWriteMemoryMappedProjectedFile.cs was determined
                // outside of this test so that the test would not hydrate the file before we access via MemoryMappedFile
                int fileTextLength = 13762;

                int size = bomOffset + fileTextLength;

                int streamAccessWriteOffset = 64;
                int randomAccessWriteOffset = 128;

                string newStreamAccessContent = "**NEW_STREAM_CONTENT**";
                string newRandomAccessConents = "&&NEW_RANDOM_CONTENT&&";

                // Read (and modify) contents using stream accessor
                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset: 0, size: size))
                {
                    streamAccessor.CanRead.ShouldEqual(true);
                    streamAccessor.CanWrite.ShouldEqual(true);

                    for (int i = 0; i < size; ++i)
                    {
                        contentsBuilder.Append((char)streamAccessor.ReadByte());
                    }

                    // Reset to the start of the stream (which will place the streamAccessor at offset in the memory file)
                    streamAccessor.Seek(streamAccessWriteOffset, SeekOrigin.Begin);
                    byte[] newContentBuffer = Encoding.ASCII.GetBytes(newStreamAccessContent);

                    streamAccessor.Write(newContentBuffer, 0, newStreamAccessContent.Length);

                    for (int i = 0; i < newStreamAccessContent.Length; ++i)
                    {
                        contentsBuilder[streamAccessWriteOffset + i] = newStreamAccessContent[i];
                    }
                }

                // Read (and modify) contents using random accessor
                using (MemoryMappedViewAccessor randomAccessor = mmf.CreateViewAccessor(offset: 0, size: size))
                {
                    randomAccessor.CanRead.ShouldEqual(true);
                    randomAccessor.CanWrite.ShouldEqual(true);

                    // Confirm the random accessor reads the same content that was read (and written) by the stream
                    // accessor
                    for (int i = 0; i < size; ++i)
                    {
                        ((char)randomAccessor.ReadByte(i)).ShouldEqual(contentsBuilder[i]);
                    }

                    // Write some new content
                    for (int i = 0; i < newRandomAccessConents.Length; ++i)
                    {
                        // Convert to byte before writing rather than writing as char, because char version will write a 16-bit
                        // unicode char
                        randomAccessor.Write(i + randomAccessWriteOffset, Convert.ToByte(newRandomAccessConents[i]));
                        ((char)randomAccessor.ReadByte(i + randomAccessWriteOffset)).ShouldEqual(newRandomAccessConents[i]);
                    }

                    for (int i = 0; i < newRandomAccessConents.Length; ++i)
                    {
                        contentsBuilder[randomAccessWriteOffset + i] = newRandomAccessConents[i];
                    }
                }

                // Verify the file one more time with a stream accessor
                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset: 0, size: size))
                {
                    for (int i = 0; i < size; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contentsBuilder[i]);
                    }
                }
            }

            // Remove the BOM before comparing with the contents of the file on disk
            contentsBuilder.Remove(0, bomOffset);

            // Confirm the new contents was written to the file
            fileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(contentsBuilder.ToString());
        }

        [TestCase, Order(5)]
        public void MoveProjectedFileToInvalidFolder()
        {
            string targetFolderName = "test_folder";
            string targetFolderVirtualPath = this.Enlistment.GetVirtualPathTo(targetFolderName);
            targetFolderVirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            string sourceFolderName = "Test_EPF_WorkingDirectoryTests";
            string testFileName = "MoveProjectedFileToInvalidFolder.config";
            string sourcePath = Path.Combine(sourceFolderName, testFileName);
            string sourceVirtualPath = this.Enlistment.GetVirtualPathTo(sourcePath);

            string newTestFileVirtualPath = Path.Combine(targetFolderVirtualPath, testFileName);

            this.fileSystem.MoveFileShouldFail(sourceVirtualPath, newTestFileVirtualPath);
            newTestFileVirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            sourceVirtualPath.ShouldBeAFile(this.fileSystem);

            targetFolderVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(6)]
        public void EnumerateAndReadDoesNotChangeEnumerationOrder()
        {
            string folderVirtualPath = this.Enlistment.GetVirtualPathTo("EnumerateAndReadTestFiles");
            this.EnumerateAndReadShouldNotChangeEnumerationOrder(folderVirtualPath);
            folderVirtualPath.ShouldBeADirectory(this.fileSystem);
            folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems();
        }

        [TestCase, Order(7)]
        public void HydratingFileUsesNameCaseFromRepo()
        {
            string fileName = "Readme.md";
            string parentFolderPath = this.Enlistment.GetVirtualPathTo(Path.GetDirectoryName(fileName));
            parentFolderPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(fileName, StringComparison.Ordinal));

            // Hydrate file with a request using different file name case except on case-sensitive filesystems
            string testFileName = FileSystemHelpers.CaseSensitiveFileSystem ? fileName : fileName.ToUpper();
            string testFilePath = this.Enlistment.GetVirtualPathTo(testFileName);
            string fileContents = testFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            // File on disk should have original case projected from repo
            parentFolderPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(fileName, StringComparison.Ordinal));
        }

        [TestCase, Order(8)]
        public void HydratingNestedFileUsesNameCaseFromRepo()
        {
            string filePath = Path.Combine("GVFS", "FastFetch", "Properties", "AssemblyInfo.cs");
            string testFilePath = FileSystemHelpers.CaseSensitiveFileSystem ? filePath : filePath.ToUpper();
            string testParentFolderVirtualPath = this.Enlistment.GetVirtualPathTo(Path.GetDirectoryName(testFilePath));
            testParentFolderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(Path.GetFileName(filePath), StringComparison.Ordinal));

            // Hydrate file with a request using different file name case except on case-sensitive filesystems
            testFilePath = this.Enlistment.GetVirtualPathTo(testFilePath);
            string fileContents = testFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            // File on disk should have original case projected from repo
            string parentFolderVirtualPath = this.Enlistment.GetVirtualPathTo(Path.GetDirectoryName(filePath));
            parentFolderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(Path.GetFileName(filePath), StringComparison.Ordinal));

            // Confirm all folders up to root have the correct case
            string parentFolderPath = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrWhiteSpace(parentFolderPath))
            {
                string folderName = Path.GetFileName(parentFolderPath);
                parentFolderPath = Path.GetDirectoryName(parentFolderPath);
                this.Enlistment.GetVirtualPathTo(parentFolderPath).ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(folderName, StringComparison.Ordinal));
            }
        }

        [TestCase, Order(9)]
        public void AppendToHydratedFileAfterRemount()
        {
            string fileToAppendEntry = "Test_EPF_WorkingDirectoryTests/WriteToHydratedFileAfterRemount.cpp";
            string virtualFilePath = this.Enlistment.GetVirtualPathTo(fileToAppendEntry);
            string fileContents = virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents();
            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, fileToAppendEntry);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            string appendedText = "Text to append";
            this.fileSystem.AppendAllText(virtualFilePath, appendedText);
            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, fileToAppendEntry);
            virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents + appendedText);
        }

        [TestCase, Order(10)]
        public void ReadDeepProjectedFile()
        {
            string testFilePath = Path.Combine("Test_EPF_WorkingDirectoryTests", "1", "2", "3", "4", "ReadDeepProjectedFile.cpp");
            this.Enlistment.GetVirtualPathTo(testFilePath).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase, Order(11)]
        public void FilePlaceHolderHasVersionInfo()
        {
            string sha = "BB1C8B9ADA90D6B8F6C88F12C6DDB07C186155BD";
            string virtualFilePath = this.Enlistment.GetVirtualPathTo("GVFlt_BugRegressionTest", "GVFlt_ModifyFileInScratchAndDir", "ModifyFileInScratchAndDir.txt");
            virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            ProcessResult revParseHeadResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse HEAD");
            string commitID = revParseHeadResult.Output.Trim();

            this.PlaceholderHasVersionInfo(virtualFilePath, CurrentPlaceholderVersion, sha).ShouldEqual(true);
        }

        [TestCase, Order(12), Ignore("Results in an access violation in the functional test on the build server")]
        public void FolderPlaceHolderHasVersionInfo()
        {
            string virtualFilePath = this.Enlistment.GetVirtualPathTo("GVFlt_BugRegressionTest", "GVFlt_ModifyFileInScratchAndDir");

            ProcessResult revParseHeadResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse HEAD");
            string commitID = revParseHeadResult.Output.Trim();

            this.PlaceholderHasVersionInfo(virtualFilePath, CurrentPlaceholderVersion, string.Empty).ShouldEqual(true);
        }

        [TestCase, Order(13)]
        [Category(Categories.GitCommands)]
        [Category(Categories.MacTODO.NeedsNewFolderCreateNotification)]
        public void FolderContentsProjectedAfterFolderCreateAndCheckout()
        {
            string folderName = "GVFlt_MultiThreadTest";

            // 54ea499de78eafb4dfd30b90e0bd4bcec26c4349 did not have the folder GVFlt_MultiThreadTest
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout 54ea499de78eafb4dfd30b90e0bd4bcec26c4349");

            // Confirm that no other test has created GVFlt_MultiThreadTest or put it in the modified files
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, folderName);

            string virtualFolderPath = this.Enlistment.GetVirtualPathTo(folderName);
            virtualFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(virtualFolderPath);

            // b3ddcf43b997cba3fbf9d2341b297e22bf48601a was the commit prior to deleting GVFLT_MultiThreadTest
            // 692765: Note that test also validates case insensitivity as GVFlt_MultiThreadTest is named GVFLT_MultiThreadTest
            //         in this commit; on case-sensitive filesystems, case sensitivity is validated instead
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout b3ddcf43b997cba3fbf9d2341b297e22bf48601a");

            string testFolderName = FileSystemHelpers.CaseSensitiveFileSystem ? "GVFLT_MultiThreadTest" : folderName;
            this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderName, "OpenForReadsSameTime", "test")).ShouldBeAFile(this.fileSystem).WithContents("123 \r\n");
            this.Enlistment.GetVirtualPathTo(Path.Combine(testFolderName, "OpenForWritesSameTime", "test")).ShouldBeAFile(this.fileSystem).WithContents("123 \r\n");
        }

        [TestCase, Order(14)]
        [Category(Categories.GitCommands)]
        public void FolderContentsCorrectAfterCreateNewFolderRenameAndCheckoutCommitWithSameFolder()
        {
            // 3a55d3b760c87642424e834228a3408796501e7c is the commit prior to adding Test_EPF_MoveRenameFileTests
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout 3a55d3b760c87642424e834228a3408796501e7c");

            // Confirm that no other test has created this folder or put it in the modified files
            string folderName = "Test_EPF_MoveRenameFileTests";
            string folder = this.Enlistment.GetVirtualPathTo(folderName);
            folder.ShouldNotExistOnDisk(this.fileSystem);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, folderName);

            // Confirm modified paths picks up renamed folder
            string newFolder = this.Enlistment.GetVirtualPathTo("newFolder");
            this.fileSystem.CreateDirectory(newFolder);
            this.fileSystem.MoveDirectory(newFolder, folder);

            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, folderName + "/");

            // Switch back to this.ControlGitRepo.Commitish and confirm that folder contents are correct
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + Properties.Settings.Default.Commitish);

            folder.ShouldBeADirectory(this.fileSystem);
            Path.Combine(folder, "ChangeNestedUnhydratedFileNameCase", "Program.cs").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFileTests.TestFileContents);
            Path.Combine(folder, "ChangeUnhydratedFileName", "Program.cs").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFileTests.TestFileContents);
            Path.Combine(folder, "MoveUnhydratedFileToDotGitFolder", "Program.cs").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFileTests.TestFileContents);
        }

        [TestCase, Order(15)]
        public void FilterNonUTF8FileName()
        {
            string encodingFilename = "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt";
            string folderVirtualPath = this.Enlistment.GetVirtualPathTo("FilenameEncoding");

            this.FolderEnumerationShouldHaveSingleEntry(folderVirtualPath, encodingFilename, null);
            this.FolderEnumerationShouldHaveSingleEntry(folderVirtualPath, encodingFilename, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
            this.FolderEnumerationShouldHaveSingleEntry(folderVirtualPath, encodingFilename, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك*");
            string testEntryExt = FileSystemHelpers.CaseSensitiveFileSystem ? "txt" : "TXT";
            string testEntryName = "ريلٌأكتوبر*." + testEntryExt;
            this.FolderEnumerationShouldHaveSingleEntry(folderVirtualPath, encodingFilename, testEntryName);

            folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithNoItems("test*");
            folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithNoItems("ريلٌأكتوب.TXT");
        }

        [TestCase, Order(16)]
        public void AllNullObjectRedownloaded()
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + this.Enlistment.Commitish);
            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse :Test_EPF_WorkingDirectoryTests/AllNullObjectRedownloaded.txt");
            string sha = revParseResult.Output.Trim();
            sha.Length.ShouldEqual(40);

            // Ensure SHA path is lowercase for case-sensitive filesystems
            string objectPathSha = FileSystemHelpers.CaseSensitiveFileSystem ? sha.ToLower() : sha;
            string objectPath = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), objectPathSha.Substring(0, 2), objectPathSha.Substring(2, 38));
            objectPath.ShouldNotExistOnDisk(this.fileSystem);

            // At this point there should be no corrupt objects
            string corruptObjectFolderPath = Path.Combine(this.Enlistment.DotGVFSRoot, "CorruptObjects");
            corruptObjectFolderPath.ShouldNotExistOnDisk(this.fileSystem);

            // Read a copy of AllNullObjectRedownloaded.txt to force the object to be downloaded
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse :Test_EPF_WorkingDirectoryTests/AllNullObjectRedownloaded_copy.txt").Output.Trim().ShouldEqual(sha);
            string testFileContents = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "AllNullObjectRedownloaded_copy.txt").ShouldBeAFile(this.fileSystem).WithContents();
            objectPath.ShouldBeAFile(this.fileSystem);

            // Set the contents of objectPath to all NULL
            FileInfo objectFileInfo = new FileInfo(objectPath);
            File.WriteAllBytes(objectPath, Enumerable.Repeat<byte>(0, (int)objectFileInfo.Length).ToArray());

            // Read the original path and verify its contents are correct
            this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "AllNullObjectRedownloaded.txt").ShouldBeAFile(this.fileSystem).WithContents(testFileContents);

            // Confirm there's a new item in the corrupt objects folder
            corruptObjectFolderPath.ShouldBeADirectory(this.fileSystem);
            FileSystemInfo badObject = corruptObjectFolderPath.ShouldBeADirectory(this.fileSystem).WithOneItem();
            (badObject as FileInfo).ShouldNotBeNull().Length.ShouldEqual(objectFileInfo.Length);
        }

        [TestCase, Order(17)]
        public void TruncatedObjectRedownloaded()
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + this.Enlistment.Commitish);
            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse :Test_EPF_WorkingDirectoryTests/TruncatedObjectRedownloaded.txt");
            string sha = revParseResult.Output.Trim();
            sha.Length.ShouldEqual(40);
            string objectPath = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), sha.Substring(0, 2), sha.Substring(2, 38));
            objectPath.ShouldNotExistOnDisk(this.fileSystem);

            string corruptObjectFolderPath = Path.Combine(this.Enlistment.DotGVFSRoot, "CorruptObjects");
            int initialCorruptObjectCount = 0;
            if (this.fileSystem.DirectoryExists(corruptObjectFolderPath))
            {
                initialCorruptObjectCount = new DirectoryInfo(corruptObjectFolderPath).EnumerateFileSystemInfos().Count();
            }

            // Read a copy of TruncatedObjectRedownloaded.txt to force the object to be downloaded
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse :Test_EPF_WorkingDirectoryTests/TruncatedObjectRedownloaded_copy.txt").Output.Trim().ShouldEqual(sha);
            string testFileContents = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "TruncatedObjectRedownloaded_copy.txt").ShouldBeAFile(this.fileSystem).WithContents();
            objectPath.ShouldBeAFile(this.fileSystem);
            string modifedFile = "Test_EPF_WorkingDirectoryTests/TruncatedObjectRedownloaded.txt";
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, modifedFile);

            // Truncate the contents of objectPath
            string tempTruncatedObjectPath = objectPath + "truncated";
            FileInfo objectFileInfo = new FileInfo(objectPath);
            long objectLength = objectFileInfo.Length;
            using (FileStream objectStream = new FileStream(objectPath, FileMode.Open))
            using (FileStream truncatedObjectStream = new FileStream(tempTruncatedObjectPath, FileMode.CreateNew))
            {
                for (int i = 0; i < (objectStream.Length - 16); ++i)
                {
                    truncatedObjectStream.WriteByte((byte)objectStream.ReadByte());
                }
            }

            this.fileSystem.DeleteFile(objectPath);
            this.fileSystem.MoveFile(tempTruncatedObjectPath, objectPath);
            tempTruncatedObjectPath.ShouldNotExistOnDisk(this.fileSystem);
            objectPath.ShouldBeAFile(this.fileSystem);
            new FileInfo(objectPath).Length.ShouldEqual(objectLength - 16);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Mac/Linux can't correct corrupt objects, but should detect them and add to ModifiedPaths.dat
                this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "TruncatedObjectRedownloaded.txt").ShouldBeAFile(this.fileSystem).WithContents();

                GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, modifedFile);
                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status",
                    $"modified:   {modifedFile}");
            }
            else
            {
                // Windows should correct a corrupt obect
                // Read the original path and verify its contents are correct
                this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "TruncatedObjectRedownloaded.txt").ShouldBeAFile(this.fileSystem).WithContents(testFileContents);

                // Confirm there's a new item in the corrupt objects folder
                corruptObjectFolderPath.ShouldBeADirectory(this.fileSystem).WithItems().Count().ShouldEqual(initialCorruptObjectCount + 1);

                // File should not be in ModifiedPaths.dat
                GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, "Test_EPF_WorkingDirectoryTests/TruncatedObjectRedownloaded.txt");
            }
        }

        [TestCase, Order(18)]
        public void CreateFileAfterTryOpenNonExistentFile()
        {
            string filePath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "CreateFileAfterTryOpenNonExistentFile_NotProjected.txt");
            string fileContents = "CreateFileAfterTryOpenNonExistentFile file contents";
            filePath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(filePath, fileContents);
            filePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        [TestCase, Order(19)]
        public void RenameFileAfterTryOpenNonExistentFile()
        {
            string filePath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "RenameFileAfterTryOpenNonExistentFile_NotProjected.txt");
            string fileContents = "CreateFileAfterTryOpenNonExistentFile file contents";
            filePath.ShouldNotExistOnDisk(this.fileSystem);

            string newFilePath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "RenameFileAfterTryOpenNonExistentFile_NewFile.txt");
            this.fileSystem.WriteAllText(newFilePath, fileContents);
            newFilePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);

            this.fileSystem.MoveFile(newFilePath, filePath);
            filePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        [TestCase, Order(20)]
        public void VerifyFileSize()
        {
            string filePath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests", "ProjectedFileHasExpectedContents.cpp");
            long fileSize = this.fileSystem.FileSize(filePath);
            fileSize.ShouldEqual(536);
        }

        private void FolderEnumerationShouldHaveSingleEntry(string folderVirtualPath, string expectedEntryName, string searchPatten)
        {
            IEnumerable<FileSystemInfo> folderEntries;
            if (string.IsNullOrEmpty(searchPatten))
            {
                folderEntries = folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems();
            }
            else
            {
                folderEntries = folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems(searchPatten);
            }

            folderEntries.Count().ShouldEqual(1);
            FileSystemInfo singleEntry = folderEntries.First();
            singleEntry.Name.ShouldEqual(expectedEntryName, $"Actual name: {singleEntry.Name} does not equal expected name {expectedEntryName}");
        }

        private void EnumerateAndReadShouldNotChangeEnumerationOrder(string folderRelativePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeTests.EnumerateAndReadDoesNotChangeEnumerationOrder(folderRelativePath).ShouldEqual(true);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string[] entries = Directory.GetFileSystemEntries(folderRelativePath);
                foreach (string entry in entries)
                {
                    File.ReadAllText(entry);
                }

                string[] postReadEntries = Directory.GetFileSystemEntries(folderRelativePath);
                Enumerable.SequenceEqual(entries, postReadEntries)
                    .ShouldBeTrue($"Entries are not the same after reading. Orignial list:\n{string.Join(",", entries)}\n\nAfter read:\n{string.Join(",", postReadEntries)}");
            }
            else
            {
                Assert.Fail("Unsupported platform");
            }
        }

        private bool PlaceholderHasVersionInfo(string relativePath, int version, string sha)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeTests.PlaceHolderHasVersionInfo(relativePath, version, sha);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TODO(Linux): Add a version of PlaceHolderHasVersionInfo that works on Linux
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO(#1360): Add a version of PlaceHolderHasVersionInfo that works on Mac
                return true;
            }
            else
            {
                Assert.Fail("Unsupported platform");
                return false;
            }
        }

        private class NativeTests
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool EnumerateAndReadDoesNotChangeEnumerationOrder(string folderVirtualPath);

            [DllImport("GVFS.NativeTests.dll", CharSet = CharSet.Ansi)]
            public static extern bool PlaceHolderHasVersionInfo(
                string virtualPath,
                int version,
                [MarshalAs(UnmanagedType.LPWStr)]string sha);
        }
    }
}
