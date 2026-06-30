using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class PrefetchBlobsOffloadTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        public PrefetchBlobsOffloadTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase, Order(1)]
        public void PrefetchBlobsMountedUsesOffload()
        {
            // With the enlistment mounted, blob prefetch should succeed
            // by offloading to the mount process (using its warm auth).
            string output = this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")}");
            output.ShouldContain("Matched blobs:");
            output.ShouldContain("Downloaded:");
        }

        [TestCase, Order(2)]
        public void PrefetchBlobsMountedReportsStats()
        {
            // Prefetch multiple files and verify stats are reported
            string output = this.Enlistment.Prefetch(
                $"--files {Path.Combine("GVFS", "GVFS", "Program.cs")};{Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")}");
            output.ShouldContain("Matched blobs:");
            output.ShouldContain("Already cached:");
            output.ShouldContain("Downloaded:");
        }

        [TestCase, Order(3)]
        public void PrefetchBlobsUnmountedFallsBackToDirectAuth()
        {
            // Unmount, then blob prefetch should fall back to direct auth
            // and still succeed. Use a file not prefetched by earlier tests
            // so the noop cache doesn't short-circuit.
            this.Enlistment.UnmountGVFS();

            try
            {
                string output = this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS.Common", "GVFSEnlistment.cs")}");
                output.ShouldContain("Matched blobs:");
                output.ShouldContain("Downloaded:");
            }
            finally
            {
                this.Enlistment.MountGVFS();
            }
        }

        [TestCase, Order(4)]
        public void PrefetchBlobsMountedWithFolders()
        {
            // Prefetch a folder while mounted
            string output = this.Enlistment.Prefetch("--folders GVFS/GVFS");
            output.ShouldContain("Matched blobs:");
        }

        [TestCase, Order(5)]
        public void PrefetchBlobsMountedAfterRemount()
        {
            // After unmount + remount, blob prefetch should work via
            // the mount process again. Since this file was already
            // prefetched in Order(1), the noop cache correctly detects
            // there's nothing new to download.
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            string output = this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")}");
            output.ShouldContain("Nothing new to prefetch.");
        }
    }
}
