using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
    public class WorkingDirectoryTests : TestsWithEnlistmentPerFixture
    {
        private const int CurrentPlaceholderVersion = 1;
        private const string TestFileContents =
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

        private FileSystemRunner fileSystem;

        public WorkingDirectoryTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void ProjectedFileHasExpectedContents()
        {
            this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests\\ProjectedFileHasExpectedContents.cpp").ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase, Order(2)]
        public void StreamAccessReadWriteMemoryMappedProjectedFile()
        {
            string filename = @"Test_EPF_WorkingDirectoryTests\StreamAccessReadWriteMemoryMappedProjectedFile.cs";
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
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
            string filename = @"Test_EPF_WorkingDirectoryTests\RandomAccessReadWriteMemoryMappedProjectedFile.cs";
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);

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
            string filename = @"Test_EPF_WorkingDirectoryTests\StreamAndRandomAccessReadWriteMemoryMappedProjectedFile.cs";
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);

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
            NativeTests.EnumerateAndReadDoesNotChangeEnumerationOrder(folderVirtualPath).ShouldEqual(true);
            folderVirtualPath.ShouldBeADirectory(this.fileSystem);
            folderVirtualPath.ShouldBeADirectory(this.fileSystem).WithItems();
        }

        [TestCase, Order(7)]
        public void HydratingFileUsesNameCaseFromRepo()
        {
            string fileName = "Readme.md";
            string parentFolderPath = this.Enlistment.GetVirtualPathTo(Path.GetDirectoryName(fileName));
            parentFolderPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(fileName, StringComparison.Ordinal));
            
            // Hydrate file with a request using different file name case
            string wrongCaseFilePath = this.Enlistment.GetVirtualPathTo(fileName.ToUpper());
            string fileContents = wrongCaseFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            // File on disk should have original case projected from repo
            parentFolderPath.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(fileName, StringComparison.Ordinal));
        }

        [TestCase, Order(8)]
        public void HydratingNestedFileUsesNameCaseFromRepo()
        {
            string filePath = "GVFS\\FastFetch\\Properties\\AssemblyInfo.cs";
            string filePathAllCaps = filePath.ToUpper();
            string parentFolderVirtualPathAllCaps = this.Enlistment.GetVirtualPathTo(Path.GetDirectoryName(filePathAllCaps));
            parentFolderVirtualPathAllCaps.ShouldBeADirectory(this.fileSystem).WithItems().ShouldContainSingle(info => info.Name.Equals(Path.GetFileName(filePath), StringComparison.Ordinal));

            // Hydrate file with a request using different file name case
            string wrongCaseFilePath = this.Enlistment.GetVirtualPathTo(filePathAllCaps);
            string fileContents = wrongCaseFilePath.ShouldBeAFile(this.fileSystem).WithContents();

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
        public void WriteToHydratedFileAfterRemount()
        {
            string fileName = "Test_EPF_WorkingDirectoryTests\\WriteToHydratedFileAfterRemount.cpp";
            string virtualFilePath = this.Enlistment.GetVirtualPathTo(fileName);
            string fileContents = virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            string appendedText = "Text to append";
            this.fileSystem.AppendAllText(virtualFilePath, appendedText);
            virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents + appendedText);
        }

        [TestCase, Order(10)]
        public void ReadDeepProjectedFile()
        {
            string testFilePath = "Test_EPF_WorkingDirectoryTests\\1\\2\\3\\4\\ReadDeepProjectedFile.cpp";
            this.Enlistment.GetVirtualPathTo(testFilePath).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase, Order(11)]
        public void FilePlaceHolderHasVersionInfo()
        {
            string sha = "BB1C8B9ADA90D6B8F6C88F12C6DDB07C186155BD";
            string virtualFilePath = this.Enlistment.GetVirtualPathTo("GVFlt_BugRegressionTest\\GVFlt_ModifyFileInScratchAndDir\\ModifyFileInScratchAndDir.txt");
            virtualFilePath.ShouldBeAFile(this.fileSystem).WithContents();

            ProcessResult revParseHeadResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse HEAD");
            string commitID = revParseHeadResult.Output.Trim();

            NativeTests.PlaceHolderHasVersionInfo(virtualFilePath, CurrentPlaceholderVersion, sha).ShouldEqual(true);
        }

        [TestCase, Order(12), Ignore("Results in an access violation in the functional test on the build server")]
        public void FolderPlaceHolderHasVersionInfo()
        {
            string virtualFilePath = this.Enlistment.GetVirtualPathTo("GVFlt_BugRegressionTest\\GVFlt_ModifyFileInScratchAndDir");

            ProcessResult revParseHeadResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "rev-parse HEAD");
            string commitID = revParseHeadResult.Output.Trim();

            NativeTests.PlaceHolderHasVersionInfo(virtualFilePath, CurrentPlaceholderVersion, string.Empty).ShouldEqual(true);
        }

        [TestCase, Order(13)]
        [Category(CategoryConstants.GitCommands)]
        public void FolderContentsProjectedAfterFolderCreateAndCheckout()
        {
            string folderName = "GVFlt_MultiThreadTest";

            // 575d597cf09b2cd1c0ddb4db21ce96979010bbcb did not have the folder GVFlt_MultiThreadTest
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout 575d597cf09b2cd1c0ddb4db21ce96979010bbcb");

            string sparseFile = this.Enlistment.GetVirtualPathTo(@".git\info\sparse-checkout");
            sparseFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldNotContain(folderName);

            string virtualFolderPath = this.Enlistment.GetVirtualPathTo(folderName);
            virtualFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(virtualFolderPath);

            // b5fd7d23706a18cff3e2b8225588d479f7e51138 was the commit prior to deleting GVFLT_MultiThreadTest
            // 692765: Note that test also validates case insensitivity as GVFlt_MultiThreadTest is named GVFLT_MultiThreadTest
            //         in this commit
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout b5fd7d23706a18cff3e2b8225588d479f7e51138");

            this.Enlistment.GetVirtualPathTo(folderName + "\\OpenForReadsSameTime\\test").ShouldBeAFile(this.fileSystem).WithContents("123 \r\n");
            this.Enlistment.GetVirtualPathTo(folderName + "\\OpenForWritesSameTime\\test").ShouldBeAFile(this.fileSystem).WithContents("123 \r\n");
        }

        [TestCase, Order(14)]
        [Category(CategoryConstants.GitCommands)]
        public void FolderContentsCorrectAfterCreateNewFolderRenameAndCheckoutCommitWithSameFolder()
        {
            // f1bce402a7a980a8320f3f235cf8c8fdade4b17a is the commit prior to adding Test_EPF_MoveRenameFolderTests
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout f1bce402a7a980a8320f3f235cf8c8fdade4b17a");

            // Confirm that no other test has created this folder or put it in the sparse-checkout
            string folderName = "Test_EPF_MoveRenameFolderTests";
            string folder = this.Enlistment.GetVirtualPathTo(folderName);
            folder.ShouldNotExistOnDisk(this.fileSystem);

            string sparseFile = this.Enlistment.GetVirtualPathTo(@".git\info\sparse-checkout");
            sparseFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldNotContain(folderName);

            // Confirm sparse-checkout picks up renamed folder
            string newFolder = this.Enlistment.GetVirtualPathTo("newFolder");
            this.fileSystem.CreateDirectory(newFolder);
            this.fileSystem.MoveDirectory(newFolder, folder);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            sparseFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(folderName);

            // Confirm that subfolders of Test_EPF_MoveRenameFolderTests are projected after switching back to this.ControlGitRepo.Commitish
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + Properties.Settings.Default.Commitish);

            folder.ShouldBeADirectory(this.fileSystem);
            (folder + @"\ChangeUnhydratedFolderName\source\ChangeUnhydratedFolderName.cpp").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFolderTests.TestFileContents);
            (folder + @"\MoveAndRenameUnhydratedFolderToNewFolder\MoveAndRenameUnhydratedFolderToNewFolder.cpp").ShouldBeAFile(this.fileSystem).WithContents(TestFileContents).WithContents(MoveRenameFolderTests.TestFileContents);
            (folder + @"\MoveFolderWithUnhydratedAndFullContents\MoveFolderWithUnhydratedAndFullContents.cpp").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFolderTests.TestFileContents);
            (folder + @"\MoveUnhydratedFolderToFullFolderInDotGitFolder\MoveUnhydratedFolderToFullFolderInDotGitFolder.cpp").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFolderTests.TestFileContents);
            (folder + @"\MoveUnhydratedFolderToVirtualNTFSFolder\MoveUnhydratedFolderToVirtualNTFSFolder.cpp").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFolderTests.TestFileContents);
            (folder + @"\RenameFolderShouldFail\source\RenameFolderShouldFail.cpp").ShouldBeAFile(this.fileSystem).WithContents(MoveRenameFolderTests.TestFileContents);
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
