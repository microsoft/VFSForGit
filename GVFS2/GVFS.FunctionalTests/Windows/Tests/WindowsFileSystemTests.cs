using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.EnlistmentPerFixture;
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
using System.Threading;

namespace GVFS.FunctionalTests.Windows.Windows.Tests
{
    [TestFixture]
    [Category(Categories.WindowsOnly)]
    public class WindowsFileSystemTests : TestsWithEnlistmentPerFixture
    {
        private enum CreationDisposition
        {
            CreateNew = 1,        // CREATE_NEW
            CreateAlways = 2,     // CREATE_ALWAYS
            OpenExisting = 3,     // OPEN_EXISTING
            OpenAlways = 4,       // OPEN_ALWAYS
            TruncateExisting = 5  // TRUNCATE_EXISTING
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Runners))]
        public void CaseOnlyRenameEmptyVirtualNTFSFolder(FileSystemRunner fileSystem, string parentFolder)
        {
            string testFolderName = Path.Combine(parentFolder, "test_folder");
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            testFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newFolderName = Path.Combine(parentFolder, "test_FOLDER");
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(newFolderName);

            // Use NativeMethods.MoveFile instead of the runner because it supports case only rename
            NativeMethods.MoveFile(testFolderVirtualPath, newFolderVirtualPath);

            newFolderVirtualPath.ShouldBeADirectory(fileSystem).WithCaseMatchingName(Path.GetFileName(newFolderName));

            fileSystem.DeleteDirectory(newFolderVirtualPath);
            newFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void CaseOnlyRenameToAllCapsEmptyVirtualNTFSFolder(FileSystemRunner fileSystem)
        {
            string testFolderName = Path.Combine("test_folder");
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            testFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newFolderName = Path.Combine("TEST_FOLDER");
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(newFolderName);

            // Use NativeMethods.MoveFile instead of the runner because it supports case only rename
            NativeMethods.MoveFile(testFolderVirtualPath, newFolderVirtualPath);

            newFolderVirtualPath.ShouldBeADirectory(fileSystem).WithCaseMatchingName(Path.GetFileName(newFolderName));

            fileSystem.DeleteDirectory(newFolderVirtualPath);
            newFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void CaseOnlyRenameTopOfVirtualNTFSFolderTree(FileSystemRunner fileSystem)
        {
            string testFolderParent = "test_folder_parent";
            string testFolderChild = "test_folder_child";
            string testFolderGrandChild = "test_folder_grandchild";
            string testFile = "test.txt";
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);

            // Create the folder tree
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

            string newFolderParentName = "test_FOLDER_PARENT";

            // Use NativeMethods.MoveFile instead of the runner because it supports case only rename
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(testFolderParent), this.Enlistment.GetVirtualPathTo(newFolderParentName));

            this.Enlistment.GetVirtualPathTo(newFolderParentName).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newFolderParentName);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Cleanup
            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(testFolderParent));

            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldNotExistOnDisk(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void CaseOnlyRenameFullDotGitFolder(FileSystemRunner fileSystem)
        {
            string testFolderName = ".git\\test_folder";
            string testFolderVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName);
            testFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);

            fileSystem.CreateDirectory(testFolderVirtualPath);
            testFolderVirtualPath.ShouldBeADirectory(fileSystem);

            string newFolderName = "test_FOLDER";
            string newFolderVirtualPath = this.Enlistment.GetVirtualPathTo(Path.Combine(".git", newFolderName));

            // Use NativeMethods.MoveFile instead of the runner because it supports case only rename
            NativeMethods.MoveFile(testFolderVirtualPath, newFolderVirtualPath);

            newFolderVirtualPath.ShouldBeADirectory(fileSystem).WithCaseMatchingName(newFolderName);

            fileSystem.DeleteDirectory(newFolderVirtualPath);
            newFolderVirtualPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void CaseOnlyRenameTopOfDotGitFullFolderTree(FileSystemRunner fileSystem)
        {
            string testFolderParent = ".git\\test_folder_parent";
            string testFolderChild = "test_folder_child";
            string testFolderGrandChild = "test_folder_grandchild";
            string testFile = "test.txt";
            this.Enlistment.GetVirtualPathTo(testFolderParent).ShouldNotExistOnDisk(fileSystem);

            // Create the folder tree
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

            string newFolderParentName = "test_FOLDER_PARENT";

            // Use NativeMethods.MoveFile instead of the runner because it supports case only rename
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(testFolderParent), this.Enlistment.GetVirtualPathTo(Path.Combine(".git", newFolderParentName)));

            this.Enlistment.GetVirtualPathTo(Path.Combine(".git", newFolderParentName)).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newFolderParentName);
            this.Enlistment.GetVirtualPathTo(realtiveChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(realtiveGrandChildFolderPath).ShouldBeADirectory(fileSystem);
            this.Enlistment.GetVirtualPathTo(relativeTestFilePath).ShouldBeAFile(fileSystem).WithContents(testFileContents);

            // Cleanup
            fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(Path.Combine(".git", newFolderParentName)));

            this.Enlistment.GetVirtualPathTo(Path.Combine(".git", newFolderParentName)).ShouldNotExistOnDisk(fileSystem);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void StreamAccessReadFromMemoryMappedVirtualNTFSFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "StreamAccessReadFromMemoryMappedVirtualNTFSFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                int offset = 0;
                int size = contents.Length;
                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset, size))
                {
                    streamAccessor.CanRead.ShouldEqual(true);

                    for (int i = 0; i < size; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contents[i]);
                    }
                }
            }

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void RandomAccessReadFromMemoryMappedVirtualNTFSFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "RandomAccessReadFromMemoryMappedVirtualNTFSFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                int offset = 0;
                int size = contents.Length;
                using (MemoryMappedViewAccessor randAccessor = mmf.CreateViewAccessor(offset, size))
                {
                    randAccessor.CanRead.ShouldEqual(true);

                    for (int i = 0; i < size; ++i)
                    {
                        ((char)randAccessor.ReadByte(i)).ShouldEqual(contents[i]);
                    }

                    for (int i = size - 1; i >= 0; --i)
                    {
                        ((char)randAccessor.ReadByte(i)).ShouldEqual(contents[i]);
                    }
                }
            }

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void StreamAccessReadWriteFromMemoryMappedVirtualNTFSFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "StreamAccessReadWriteFromMemoryMappedVirtualNTFSFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                int offset = 64;
                int size = contents.Length;
                string newContent = "**NEWCONTENT**";

                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset, size - offset))
                {
                    streamAccessor.CanRead.ShouldEqual(true);
                    streamAccessor.CanWrite.ShouldEqual(true);

                    for (int i = offset; i < size - offset; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contents[i]);
                    }

                    // Reset to the start of the stream (which will place the streamAccessor at offset in the memory file)
                    streamAccessor.Seek(0, SeekOrigin.Begin);
                    byte[] newContentBuffer = Encoding.ASCII.GetBytes(newContent);

                    streamAccessor.Write(newContentBuffer, 0, newContent.Length);

                    for (int i = 0; i < newContent.Length; ++i)
                    {
                        contentsBuilder[offset + i] = newContent[i];
                    }

                    contents = contentsBuilder.ToString();
                }

                // Verify the file has the new contents inserted into it
                using (MemoryMappedViewStream streamAccessor = mmf.CreateViewStream(offset: 0, size: size))
                {
                    for (int i = 0; i < size; ++i)
                    {
                        streamAccessor.ReadByte().ShouldEqual(contents[i]);
                    }
                }
            }

            // Confirm the new contents was written to disk
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void RandomAccessReadWriteFromMemoryMappedVirtualNTFSFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "RandomAccessReadWriteFromMemoryMappedVirtualNTFSFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath))
            {
                int offset = 64;
                int size = contents.Length;
                string newContent = "**NEWCONTENT**";

                using (MemoryMappedViewAccessor randomAccessor = mmf.CreateViewAccessor(offset, size - offset))
                {
                    randomAccessor.CanRead.ShouldEqual(true);
                    randomAccessor.CanWrite.ShouldEqual(true);

                    for (int i = 0; i < size - offset; ++i)
                    {
                        ((char)randomAccessor.ReadByte(i)).ShouldEqual(contents[i + offset]);
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
                        contentsBuilder[offset + i] = newContent[i];
                    }

                    contents = contentsBuilder.ToString();
                }

                // Verify the file has the new contents inserted into it
                using (MemoryMappedViewAccessor randomAccessor = mmf.CreateViewAccessor(offset: 0, size: size))
                {
                    for (int i = 0; i < size; ++i)
                    {
                        ((char)randomAccessor.ReadByte(i)).ShouldEqual(contents[i]);
                    }
                }
            }

            // Confirm the new contents was written to disk
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void StreamAccessToExistingMemoryMappedFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "StreamAccessToExistingMemoryMappedFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();
            int size = contents.Length;

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            string memoryMapFileName = "StreamAccessFile";
            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath, FileMode.Open, memoryMapFileName))
            {
                Thread[] threads = new Thread[4];
                bool keepRunning = true;
                for (int i = 0; i < threads.Length; ++i)
                {
                    int myIndex = i;
                    threads[i] = new Thread(() =>
                    {
                        // Create random seeks (seeded for repeatability)
                        Random randNum = new Random(myIndex);

                        using (MemoryMappedFile threadFile = MemoryMappedFile.OpenExisting(memoryMapFileName))
                        {
                            while (keepRunning)
                            {
                                // Pick an offset somewhere in the first half of the file
                                int offset = randNum.Next(size / 2);

                                using (MemoryMappedViewStream streamAccessor = threadFile.CreateViewStream(offset, size - offset))
                                {
                                    for (int j = 0; j < size - offset; ++j)
                                    {
                                        streamAccessor.ReadByte().ShouldEqual(contents[j + offset]);
                                    }
                                }
                            }
                        }
                    });

                    threads[i].Start();
                }

                Thread.Sleep(500);
                keepRunning = false;

                for (int i = 0; i < threads.Length; ++i)
                {
                    threads[i].Join();
                }
            }

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void RandomAccessToExistingMemoryMappedFile(string parentFolder)
        {
            // Use SystemIORunner as the text we are writing is too long to pass to the command line
            FileSystemRunner fileSystem = new SystemIORunner();

            string filename = Path.Combine(parentFolder, "RandomAccessToExistingMemoryMappedFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            StringBuilder contentsBuilder = new StringBuilder();
            while (contentsBuilder.Length < 4096 * 2)
            {
                contentsBuilder.Append(Guid.NewGuid().ToString());
            }

            string contents = contentsBuilder.ToString();
            int size = contents.Length;

            fileSystem.WriteAllText(fileVirtualPath, contents);
            fileVirtualPath.ShouldBeAFile(fileSystem).WithContents(contents);

            string memoryMapFileName = "RandomAccessFile";
            using (MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fileVirtualPath, FileMode.Open, memoryMapFileName))
            {
                Thread[] threads = new Thread[4];
                bool keepRunning = true;
                for (int i = 0; i < threads.Length; ++i)
                {
                    int myIndex = i;
                    threads[i] = new Thread(() =>
                    {
                        // Create random seeks (seeded for repeatability)
                        Random randNum = new Random(myIndex);

                        using (MemoryMappedFile threadFile = MemoryMappedFile.OpenExisting(memoryMapFileName))
                        {
                            while (keepRunning)
                            {
                                // Pick an offset somewhere in the first half of the file
                                int offset = randNum.Next(size / 2);

                                using (MemoryMappedViewAccessor randomAccessor = threadFile.CreateViewAccessor(offset, size - offset))
                                {
                                    for (int j = 0; j < size - offset; ++j)
                                    {
                                        ((char)randomAccessor.ReadByte(j)).ShouldEqual(contents[j + offset]);
                                    }
                                }
                            }
                        }
                    });

                    threads[i].Start();
                }

                Thread.Sleep(500);
                keepRunning = false;

                for (int i = 0; i < threads.Length; ++i)
                {
                    threads[i].Join();
                }
            }

            fileSystem.DeleteFile(fileVirtualPath);
            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void NativeReadAndWriteSeparateHandles(string parentFolder)
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine(parentFolder, "NativeReadAndWriteSeparateHandles");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.ReadAndWriteSeparateHandles(fileVirtualPath).ShouldEqual(true);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void NativeReadAndWriteSameHandle(string parentFolder)
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine(parentFolder, "NativeReadAndWriteSameHandle");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.ReadAndWriteSameHandle(fileVirtualPath, synchronousIO: false).ShouldEqual(true);

            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.ReadAndWriteSameHandle(fileVirtualPath, synchronousIO: true).ShouldEqual(true);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void NativeReadAndWriteRepeatedly(string parentFolder)
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine(parentFolder, "NativeReadAndWriteRepeatedly");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.ReadAndWriteRepeatedly(fileVirtualPath, synchronousIO: false).ShouldEqual(true);

            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.ReadAndWriteRepeatedly(fileVirtualPath, synchronousIO: true).ShouldEqual(true);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void NativeRemoveReadOnlyAttribute(string parentFolder)
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine(parentFolder, "NativeRemoveReadOnlyAttribute");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.RemoveReadOnlyAttribute(fileVirtualPath).ShouldEqual(true);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCaseSource(typeof(FileRunnersAndFolders), nameof(FileRunnersAndFolders.Folders))]
        public void NativeCannotWriteToReadOnlyFile(string parentFolder)
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string filename = Path.Combine(parentFolder, "NativeCannotWriteToReadOnlyFile");
            string fileVirtualPath = this.Enlistment.GetVirtualPathTo(filename);
            fileVirtualPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.CannotWriteToReadOnlyFile(fileVirtualPath).ShouldEqual(true);

            FileRunnersAndFolders.ShouldNotExistOnDisk(this.Enlistment, fileSystem, filename, parentFolder);
        }

        [TestCase]
        public void NativeEnumerationErrorsMatchNTFS()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string nonExistentVirtualPath = this.Enlistment.GetVirtualPathTo("this_does_not_exist");
            nonExistentVirtualPath.ShouldNotExistOnDisk(fileSystem);
            string nonExistentPhysicalPath = Path.Combine(this.Enlistment.DotGVFSRoot, "this_does_not_exist");
            nonExistentPhysicalPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.EnumerationErrorsMatchNTFSForNonExistentFolder(nonExistentVirtualPath, nonExistentPhysicalPath).ShouldEqual(true);
        }

        [TestCase]
        public void NativeEnumerationErrorsMatchNTFSForNestedFolder()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            this.Enlistment.GetVirtualPathTo("GVFS").ShouldBeADirectory(fileSystem);
            string nonExistentVirtualPath = this.Enlistment.GetVirtualPathTo("GVFS\\this_does_not_exist");
            nonExistentVirtualPath.ShouldNotExistOnDisk(fileSystem);

            this.Enlistment.DotGVFSRoot.ShouldBeADirectory(fileSystem);
            string nonExistentPhysicalPath = Path.Combine(this.Enlistment.DotGVFSRoot, "this_does_not_exist");
            nonExistentPhysicalPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.EnumerationErrorsMatchNTFSForNonExistentFolder(nonExistentVirtualPath, nonExistentPhysicalPath).ShouldEqual(true);
        }

        [TestCase]
        public void NativeEnumerationDotGitFolderErrorsMatchNTFS()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string nonExistentVirtualPath = this.Enlistment.GetVirtualPathTo(".git\\this_does_not_exist");
            nonExistentVirtualPath.ShouldNotExistOnDisk(fileSystem);
            string nonExistentPhysicalPath = Path.Combine(this.Enlistment.DotGVFSRoot, "this_does_not_exist");
            nonExistentPhysicalPath.ShouldNotExistOnDisk(fileSystem);

            NativeTests.EnumerationErrorsMatchNTFSForNonExistentFolder(nonExistentVirtualPath, nonExistentPhysicalPath).ShouldEqual(true);
        }

        [TestCase]
        public void NativeEnumerationErrorsMatchNTFSForEmptyNewFolder()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string newVirtualFolderPath = this.Enlistment.GetVirtualPathTo("new_folder");
            newVirtualFolderPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateDirectory(newVirtualFolderPath);
            newVirtualFolderPath.ShouldBeADirectory(fileSystem);

            string newPhysicalFolderPath = Path.Combine(this.Enlistment.DotGVFSRoot, "new_folder");
            newPhysicalFolderPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateDirectory(newPhysicalFolderPath);
            newPhysicalFolderPath.ShouldBeADirectory(fileSystem);

            NativeTests.EnumerationErrorsMatchNTFSForEmptyFolder(newVirtualFolderPath, newPhysicalFolderPath).ShouldEqual(true);

            fileSystem.DeleteDirectory(newVirtualFolderPath);
            newVirtualFolderPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.DeleteDirectory(newPhysicalFolderPath);
            newPhysicalFolderPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCase]
        public void NativeDeleteEmptyFolderWithFileDispositionOnClose()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string newVirtualFolderPath = this.Enlistment.GetVirtualPathTo("new_folder");
            newVirtualFolderPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateDirectory(newVirtualFolderPath);
            newVirtualFolderPath.ShouldBeADirectory(fileSystem);

            NativeTests.CanDeleteEmptyFolderWithFileDispositionOnClose(newVirtualFolderPath).ShouldEqual(true);

            newVirtualFolderPath.ShouldNotExistOnDisk(fileSystem);
        }

        [TestCase]
        public void NativeQueryDirectoryFileRestartScanResetsFilter()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string folderPath = this.Enlistment.GetVirtualPathTo("EnumerateAndReadTestFiles");
            folderPath.ShouldBeADirectory(fileSystem);

            NativeTests.QueryDirectoryFileRestartScanResetsFilter(folderPath).ShouldEqual(true);
        }

        [TestCase]
        public void ErrorWhenPathTreatsFileAsFolderMatchesNTFS_VirtualProjFSPath()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string existingFileVirtualPath = this.Enlistment.GetVirtualPathTo("ErrorWhenPathTreatsFileAsFolderMatchesNTFS\\virtual");
            string existingFilePhysicalPath = this.CreateFileInPhysicalPath(fileSystem);

            foreach (CreationDisposition creationDispostion in Enum.GetValues(typeof(CreationDisposition)))
            {
                NativeTests.ErrorWhenPathTreatsFileAsFolderMatchesNTFS(existingFileVirtualPath, existingFilePhysicalPath, (int)creationDispostion).ShouldEqual(true);
            }
        }

        [TestCase]
        public void ErrorWhenPathTreatsFileAsFolderMatchesNTFS_PartialProjFSPath()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string existingFileVirtualPath = this.Enlistment.GetVirtualPathTo("ErrorWhenPathTreatsFileAsFolderMatchesNTFS\\partial");
            existingFileVirtualPath.ShouldBeAFile(fileSystem);
            fileSystem.ReadAllText(existingFileVirtualPath);
            string existingFilePhysicalPath = this.CreateFileInPhysicalPath(fileSystem);

            foreach (CreationDisposition creationDispostion in Enum.GetValues(typeof(CreationDisposition)))
            {
                NativeTests.ErrorWhenPathTreatsFileAsFolderMatchesNTFS(existingFileVirtualPath, existingFilePhysicalPath, (int)creationDispostion).ShouldEqual(true);
            }
        }

        [TestCase]
        public void ErrorWhenPathTreatsFileAsFolderMatchesNTFS_FullProjFSPath()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;

            string existingFileVirtualPath = this.Enlistment.GetVirtualPathTo("ErrorWhenPathTreatsFileAsFolderMatchesNTFS\\full");
            existingFileVirtualPath.ShouldBeAFile(fileSystem);
            fileSystem.AppendAllText(existingFileVirtualPath, "extra text");
            string existingFilePhysicalPath = this.CreateFileInPhysicalPath(fileSystem);

            foreach (CreationDisposition creationDispostion in Enum.GetValues(typeof(CreationDisposition)))
            {
                NativeTests.ErrorWhenPathTreatsFileAsFolderMatchesNTFS(existingFileVirtualPath, existingFilePhysicalPath, (int)creationDispostion).ShouldEqual(true);
            }
        }

        [TestCase]
        public void EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete()
        {
            NativeTrailingSlashTests.EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_ModifyFileInScratchAndDir()
        {
            ProjFS_BugRegressionTest.ProjFS_ModifyFileInScratchAndDir(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_RMDIRTest1()
        {
            ProjFS_BugRegressionTest.ProjFS_RMDIRTest1(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_RMDIRTest2()
        {
            ProjFS_BugRegressionTest.ProjFS_RMDIRTest2(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_RMDIRTest3()
        {
            ProjFS_BugRegressionTest.ProjFS_RMDIRTest3(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_RMDIRTest4()
        {
            ProjFS_BugRegressionTest.ProjFS_RMDIRTest4(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_RMDIRTest5()
        {
            ProjFS_BugRegressionTest.ProjFS_RMDIRTest5(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeepNonExistFileUnderPartial()
        {
            ProjFS_BugRegressionTest.ProjFS_DeepNonExistFileUnderPartial(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SupersededReparsePoint()
        {
            ProjFS_BugRegressionTest.ProjFS_SupersededReparsePoint(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteVirtualFile_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteVirtualFile_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteVirtualFile_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteVirtualFile_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeletePlaceholder_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeletePlaceholder_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeletePlaceholder_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeletePlaceholder_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteFullFile_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteFullFile_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteFullFile_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteFullFile_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteLocalFile_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteLocalFile_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteLocalFile_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteLocalFile_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNotExistFile_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteNotExistFile_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNotExistFile_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteNotExistFile_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNonRootVirtualFile_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteNonRootVirtualFile_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNonRootVirtualFile_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteNonRootVirtualFile_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteFileOutsideVRoot_SetDisposition()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteFileOutsideVRoot_SetDisposition(Path.GetDirectoryName(this.Enlistment.RepoRoot)).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteFileOutsideVRoot_DeleteOnClose()
        {
            ProjFS_DeleteFileTest.ProjFS_DeleteFileOutsideVRoot_DeleteOnClose(Path.GetDirectoryName(this.Enlistment.RepoRoot)).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteVirtualNonEmptyFolder_SetDisposition()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteVirtualNonEmptyFolder_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteVirtualNonEmptyFolder_DeleteOnClose()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteVirtualNonEmptyFolder_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeletePlaceholderNonEmptyFolder_SetDisposition()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeletePlaceholderNonEmptyFolder_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeletePlaceholderNonEmptyFolder_DeleteOnClose()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeletePlaceholderNonEmptyFolder_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteLocalEmptyFolder_SetDisposition()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteLocalEmptyFolder_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteLocalEmptyFolder_DeleteOnClose()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteLocalEmptyFolder_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNonRootVirtualFolder_SetDisposition()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteNonRootVirtualFolder_SetDisposition(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteNonRootVirtualFolder_DeleteOnClose()
        {
            ProjFS_DeleteFolderTest.ProjFS_DeleteNonRootVirtualFolder_DeleteOnClose(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumEmptyFolder()
        {
            ProjFS_DirEnumTest.ProjFS_EnumEmptyFolder(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumFolderWithOneFileInRepo()
        {
            ProjFS_DirEnumTest.ProjFS_EnumFolderWithOneFileInPackage(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumFolderWithOneFileInRepoBeforeScratchFile()
        {
            ProjFS_DirEnumTest.ProjFS_EnumFolderWithOneFileInBoth(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumFolderWithOneFileInRepoAfterScratchFile()
        {
            ProjFS_DirEnumTest.ProjFS_EnumFolderWithOneFileInBoth1(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumFolderDeleteExistingFile()
        {
            ProjFS_DirEnumTest.ProjFS_EnumFolderDeleteExistingFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumFolderSmallBuffer()
        {
            ProjFS_DirEnumTest.ProjFS_EnumFolderSmallBuffer(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumTestNoMoreNoSuchReturnCodes()
        {
            ProjFS_DirEnumTest.ProjFS_EnumTestNoMoreNoSuchReturnCodes(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_EnumTestQueryDirectoryFileRestartScanProjectedFile()
        {
            ProjFS_DirEnumTest.ProjFS_EnumTestQueryDirectoryFileRestartScanProjectedFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_ModifyFileInScratchAndCheckLastWriteTime()
        {
            ProjFS_FileAttributeTest.ProjFS_ModifyFileInScratchAndCheckLastWriteTime(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_FileSize()
        {
            ProjFS_FileAttributeTest.ProjFS_FileSize(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_ModifyFileInScratchAndCheckFileSize()
        {
            ProjFS_FileAttributeTest.ProjFS_ModifyFileInScratchAndCheckFileSize(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_FileAttributes()
        {
            ProjFS_FileAttributeTest.ProjFS_FileAttributes(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OneEAAttributeWillPass()
        {
            ProjFS_FileEATest.ProjFS_OneEAAttributeWillPass(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OpenRootFolder()
        {
            ProjFS_FileOperationTest.ProjFS_OpenRootFolder(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_WriteAndVerify()
        {
            ProjFS_FileOperationTest.ProjFS_WriteAndVerify(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_DeleteExistingFile()
        {
            ProjFS_FileOperationTest.ProjFS_DeleteExistingFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OpenNonExistingFile()
        {
            ProjFS_FileOperationTest.ProjFS_OpenNonExistingFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_NoneToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_NoneToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_PartialToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_PartialToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_FullToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_FullToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_LocalToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_LocalToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToVirtual()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToVirtual(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToVirtualFileNameChanged()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToVirtualFileNameChanged(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToPartial()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToPartial(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_PartialToPartial()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_PartialToPartial(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_LocalToVirtual()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_LocalToVirtual(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToVirtualIntermidiateDirNotExist()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToVirtualIntermidiateDirNotExist(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToNoneIntermidiateDirNotExist()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToNoneIntermidiateDirNotExist(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_OutsideToNone()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_OutsideToNone(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_OutsideToVirtual()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_OutsideToVirtual(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_OutsideToPartial()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_OutsideToPartial(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_NoneToOutside()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_NoneToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_VirtualToOutside()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_VirtualToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [Ignore("Disable this test until we can surface native test errors, see #454")]
        [TestCase]
        public void Native_ProjFS_MoveFile_PartialToOutside()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_PartialToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFile_OutsideToOutside()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_OutsideToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        [Ignore("Disabled while ProjFS fixes a regression")]
        public void Native_ProjFS_MoveFile_LongFileName()
        {
            ProjFS_MoveFileTest.ProjFS_MoveFile_LongFileName(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_NoneToNone()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_NoneToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_VirtualToNone()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_VirtualToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_PartialToNone()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_PartialToNone(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_VirtualToVirtual()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_VirtualToVirtual(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_VirtualToPartial()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_VirtualToPartial(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_OutsideToNone()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_OutsideToNone(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_OutsideToVirtual()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_OutsideToVirtual(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_NoneToOutside()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_NoneToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_VirtualToOutside()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_VirtualToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_MoveFolder_OutsideToOutside()
        {
            ProjFS_MoveFolderTest.ProjFS_MoveFolder_OutsideToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OpenForReadsSameTime()
        {
            ProjFS_MultiThreadTest.ProjFS_OpenForReadsSameTime(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OpenMultipleFilesForReadsSameTime()
        {
            ProjFS_MultiThreadTest.ProjFS_OpenMultipleFilesForReadsSameTime(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_OpenForWritesSameTime()
        {
            ProjFS_MultiThreadTest.ProjFS_OpenForWritesSameTime(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_ToVirtualFile()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_ToVirtualFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_ToPlaceHolder()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_ToPlaceHolder(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_ToFullFile()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_ToFullFile(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_ToNonExistFileWillFail()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_ToNonExistFileWillFail(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_NameAlreadyExistWillFail()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_NameAlreadyExistWillFail(this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_FromOutside()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_FromOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        [TestCase]
        public void Native_ProjFS_SetLink_ToOutside()
        {
            ProjFS_SetLinkTest.ProjFS_SetLink_ToOutside(Path.GetDirectoryName(this.Enlistment.RepoRoot), this.Enlistment.RepoRoot).ShouldEqual(true);
        }

        private string CreateFileInPhysicalPath(FileSystemRunner fileSystem)
        {
            string existingFilePhysicalPath = Path.Combine(this.Enlistment.DotGVFSRoot, "existingFileTest.txt");
            fileSystem.WriteAllText(existingFilePhysicalPath, "File for testing");
            existingFilePhysicalPath.ShouldBeAFile(fileSystem);
            return existingFilePhysicalPath;
        }

        private class NativeTests
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ReadAndWriteSeparateHandles(string fileVirtualPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ReadAndWriteSameHandle(string fileVirtualPath, bool synchronousIO);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ReadAndWriteRepeatedly(string fileVirtualPath, bool synchronousIO);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool RemoveReadOnlyAttribute(string fileVirtualPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool CannotWriteToReadOnlyFile(string fileVirtualPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool EnumerationErrorsMatchNTFSForNonExistentFolder(string nonExistentVirtualPath, string nonExistentPhysicalPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool EnumerationErrorsMatchNTFSForEmptyFolder(string emptyFolderVirtualPath, string emptyFolderPhysicalPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool CanDeleteEmptyFolderWithFileDispositionOnClose(string emptyFolderPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool QueryDirectoryFileRestartScanResetsFilter(string folderPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ErrorWhenPathTreatsFileAsFolderMatchesNTFS(string filePath, string fileNTFSPath, int creationDisposition);
        }

        private class NativeTrailingSlashTests
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete(string virtualRootPath);
        }

        private class ProjFS_BugRegressionTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_ModifyFileInScratchAndDir(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_RMDIRTest1(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_RMDIRTest2(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_RMDIRTest3(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_RMDIRTest4(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_RMDIRTest5(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeepNonExistFileUnderPartial(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SupersededReparsePoint(string virtualRootPath);
        }

        private class ProjFS_DeleteFileTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteVirtualFile_SetDisposition(string enumFolderSmallBufferPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteVirtualFile_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeletePlaceholder_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeletePlaceholder_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteFullFile_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteFullFile_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteLocalFile_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteLocalFile_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNotExistFile_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNotExistFile_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNonRootVirtualFile_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNonRootVirtualFile_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteFileOutsideVRoot_SetDisposition(string pathOutsideRepo);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteFileOutsideVRoot_DeleteOnClose(string pathOutsideRepo);
        }

        private class ProjFS_DeleteFolderTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteVirtualNonEmptyFolder_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteVirtualNonEmptyFolder_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeletePlaceholderNonEmptyFolder_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeletePlaceholderNonEmptyFolder_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteLocalEmptyFolder_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteLocalEmptyFolder_DeleteOnClose(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNonRootVirtualFolder_SetDisposition(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteNonRootVirtualFolder_DeleteOnClose(string virtualRootPath);
        }

        private class ProjFS_DirEnumTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumEmptyFolder(string emptyFolderPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumFolderWithOneFileInPackage(string enumFolderWithOneFileInRepoPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumFolderWithOneFileInBoth(string enumFolderWithOneFileInRepoBeforeScratchPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumFolderWithOneFileInBoth1(string enumFolderWithOneFileInRepoAfterScratchPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumFolderDeleteExistingFile(string enumFolderDeleteExistingFilePath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumFolderSmallBuffer(string enumFolderSmallBufferPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumTestNoMoreNoSuchReturnCodes(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_EnumTestQueryDirectoryFileRestartScanProjectedFile(string virtualRootPath);
        }

        private class ProjFS_FileAttributeTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_ModifyFileInScratchAndCheckLastWriteTime(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_FileSize(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_ModifyFileInScratchAndCheckFileSize(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_FileAttributes(string virtualRootPath);
        }

        private class ProjFS_FileEATest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OneEAAttributeWillPass(string virtualRootPath);
        }

        private class ProjFS_FileOperationTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OpenRootFolder(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_WriteAndVerify(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_DeleteExistingFile(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OpenNonExistingFile(string virtualRootPath);
        }

        private class ProjFS_MoveFileTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_NoneToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_PartialToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_FullToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_LocalToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToVirtual(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToVirtualFileNameChanged(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToPartial(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_PartialToPartial(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_LocalToVirtual(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToVirtualIntermidiateDirNotExist(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToNoneIntermidiateDirNotExist(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_OutsideToNone(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_OutsideToVirtual(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_OutsideToPartial(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_NoneToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_VirtualToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_PartialToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_OutsideToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFile_LongFileName(string virtualRootPath);
        }

        private class ProjFS_MoveFolderTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_NoneToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_VirtualToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_PartialToNone(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_VirtualToVirtual(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_VirtualToPartial(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_OutsideToNone(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_OutsideToVirtual(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_NoneToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_VirtualToOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_MoveFolder_OutsideToOutside(string pathOutsideRepo, string virtualRootPath);
        }

        private class ProjFS_MultiThreadTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OpenForReadsSameTime(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OpenForWritesSameTime(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_OpenMultipleFilesForReadsSameTime(string virtualRootPath);
        }

        private class ProjFS_SetLinkTest
        {
            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_ToVirtualFile(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_ToPlaceHolder(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_ToFullFile(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_ToNonExistFileWillFail(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_NameAlreadyExistWillFail(string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_FromOutside(string pathOutsideRepo, string virtualRootPath);

            [DllImport("GVFS.NativeTests.dll")]
            public static extern bool ProjFS_SetLink_ToOutside(string pathOutsideRepo, string virtualRootPath);
        }

        private class FileRunnersAndFolders
        {
            public const string TestFolders = "Folders";
            public const string TestRunners = "Runners";
            public const string DotGitFolder = ".git";

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
