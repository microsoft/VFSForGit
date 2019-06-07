using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    // TODO 469238: Elaborate on these tests?
    [TestFixture]
    public class MultithreadedReadWriteTests : TestsWithEnlistmentPerFixture
    {
        private int nativeEWouldBlock = 0;

        public MultithreadedReadWriteTests()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // #define EAGAIN      11      /* Resource temporarily unavailable */
                // #define EWOULDBLOCK EAGAIN  /* Operation would block */
                this.nativeEWouldBlock = 11;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // #define EAGAIN      35      /* Resource temporarily unavailable */
                // #define EWOULDBLOCK EAGAIN  /* Operation would block */
                this.nativeEWouldBlock = 35;
            }
        }

        [TestCase, Order(1)]
        public void CanReadVirtualFileInParallel()
        {
            // Note: This test MUST go first, or else it needs to ensure that it is reading a unique path compared to the
            // other tests in this class. That applies to every directory in the path, as well as the leaf file name.
            // Otherwise, this test loses most of its value because there will be no races occurring on creating the
            // placeholder directories, enumerating them, and then creating a placeholder file and hydrating it.

            string fileName = Path.Combine("GVFS", "GVFS.FunctionalTests", "Tests", "LongRunningEnlistment", "GitMoveRenameTests.cs");
            string virtualPath = this.Enlistment.GetVirtualPathTo(fileName);

            Exception readException = null;

            Thread[] threads = new Thread[128];
            for (int i = 0; i < threads.Length; ++i)
            {
                threads[i] = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            FileSystemRunner.DefaultRunner.ReadAllText(virtualPath).ShouldBeNonEmpty();
                            break;
                        }
                        catch (IOException) when (Marshal.GetLastWin32Error() == this.nativeEWouldBlock && this.nativeEWouldBlock > 0)
                        {
                            // ignore EAGAIN
                        }
                        catch (Exception e)
                        {
                            readException = e;
                            break;
                        }
                    }
                });

                threads[i].Start();
            }

            for (int i = 0; i < threads.Length; ++i)
            {
                threads[i].Join();
            }

            readException.ShouldBeNull("At least one of the reads failed");
        }

        [TestCase, Order(2)]
        public void CanReadHydratedPlaceholderInParallel()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string fileName = Path.Combine("GVFS", "GVFS.FunctionalTests", "Tests", "LongRunningEnlistment", "WorkingDirectoryTests.cs");
            string virtualPath = this.Enlistment.GetVirtualPathTo(fileName);
            virtualPath.ShouldBeAFile(fileSystem);

            // Not using the runner because reading specific bytes isn't common
            // Can't use ReadAllText because it will remove some bytes that the stream won't.
            byte[] actualContents = File.ReadAllBytes(virtualPath);

            Thread[] threads = new Thread[4];

            // Readers
            bool keepRunning = true;
            for (int i = 0; i < threads.Length; ++i)
            {
                int myIndex = i;
                threads[i] = new Thread(() =>
                {
                    // Create random seeks (seeded for repeatability)
                    Random randy = new Random(myIndex);

                    // Small buffer so we hit the drive a lot.
                    // Block larger than the buffer to hit the drive more
                    const int SmallBufferSize = 128;
                    const int LargerBlockSize = SmallBufferSize * 10;

                    using (Stream reader = new FileStream(virtualPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, SmallBufferSize, false))
                    {
                        while (keepRunning)
                        {
                            byte[] block = new byte[LargerBlockSize];

                            // Always try to grab a full block (easier for asserting)
                            int position = randy.Next((int)reader.Length - block.Length - 1);

                            reader.Position = position;
                            reader.Read(block, 0, block.Length).ShouldEqual(block.Length);
                            block.ShouldEqual(actualContents, position, block.Length);
                        }
                    }
                });

                threads[i].Start();
            }

            Thread.Sleep(2500);
            keepRunning = false;

            for (int i = 0; i < threads.Length; ++i)
            {
                threads[i].Join();
            }
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        [Category(Categories.LinuxTODO.NeedsConsistentBufferedWrites)]
        [Order(3)]
        public void CanReadWriteAFileInParallel(FileSystemRunner fileSystem)
        {
            string fileName = @"CanReadWriteAFileInParallel";
            string virtualPath = this.Enlistment.GetVirtualPathTo(fileName);

            // Create the file new each time.
            virtualPath.ShouldNotExistOnDisk(fileSystem);
            File.Create(virtualPath).Dispose();

            bool keepRunning = true;
            Thread[] threads = new Thread[4];
            StringBuilder[] fileContents = new StringBuilder[4];

            Exception writeException = null;

            // Writer
            fileContents[0] = new StringBuilder();
            threads[0] = new Thread(() =>
            {
                DateTime start = DateTime.Now;
                Random r = new Random(0); // Seeded for repeatability
                while ((DateTime.Now - start).TotalSeconds < 2.5)
                {
                    string newChar = r.Next(10).ToString();
                    try
                    {
                        fileSystem.AppendAllText(virtualPath, newChar);
                        fileContents[0].Append(newChar);
                    }
                    catch (IOException) when (Marshal.GetLastWin32Error() == this.nativeEWouldBlock && this.nativeEWouldBlock > 0)
                    {
                        // ignore EAGAIN
                    }
                    catch (Exception e)
                    {
                        writeException = e;
                    }
                    Thread.Yield();
                }

                keepRunning = false;
            });

            Exception readException = null;

            // Readers
            for (int i = 1; i < threads.Length; ++i)
            {
                int myIndex = i;
                fileContents[i] = new StringBuilder();
                threads[i] = new Thread(() =>
                {
                    bool retry = true;
                    while (retry)
                    {
                        Stream readStream = null;
                        try
                        {
                            readStream = File.Open(virtualPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            retry = false;
                            using (StreamReader reader = new StreamReader(readStream, true))
                            {
                                while (keepRunning)
                                {
                                    Thread.Yield();
                                    fileContents[myIndex].Append(reader.ReadToEnd());
                                }

                                // Catch the last write that might have escaped us
                                fileContents[myIndex].Append(reader.ReadToEnd());
                            }
                        }
                        catch (IOException) when (Marshal.GetLastWin32Error() == this.nativeEWouldBlock && this.nativeEWouldBlock > 0)
                        {
                            // ignore EAGAIN
                        }
                        catch (Exception e)
                        {
                            readException = e;
                        }
                        finally
                        {
                            if (readStream != null)
                            {
                                ((IDisposable)readStream).Dispose();
                            }
                        }
                    }
                });
            }

            foreach (Thread thread in threads)
            {
                thread.Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            for (int i = 1; i < threads.Length; ++i)
            {
                fileContents[i].ToString().ShouldEqual(fileContents[0].ToString());
            }

            fileSystem.DeleteFile(virtualPath);

            readException.ShouldBeNull("At least one of the reads failed");
            writeException.ShouldBeNull("At least one of the writes failed");
        }
    }
}
