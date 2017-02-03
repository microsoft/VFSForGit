using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class MultithreadedReadWriteTests : TestsWithLongRunningEnlistment
    {
        [TestCase]
        public void CanReadUnhydratedFileInParallelWithoutTearing()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string fileName = @"GVFS\GVFS.FunctionalTests\Tests\LongRunningEnlistment\WorkingDirectoryTests.cs";
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

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
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

            // Writer
            fileContents[0] = new StringBuilder();
            threads[0] = new Thread(() =>
            {
                DateTime start = DateTime.Now;
                Random r = new Random(0); // Seeded for repeatability
                while ((DateTime.Now - start).TotalSeconds < 2.5)
                {
                    string newChar = r.Next(10).ToString();
                    fileSystem.AppendAllText(virtualPath, newChar);
                    fileContents[0].Append(newChar);
                    Thread.Yield();
                }

                keepRunning = false;
            });

            // Readers
            for (int i = 1; i < threads.Length; ++i)
            {
                int myIndex = i;
                fileContents[i] = new StringBuilder();
                threads[i] = new Thread(() =>
                {
                    using (Stream readStream = File.Open(virtualPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
        }
    }
}
