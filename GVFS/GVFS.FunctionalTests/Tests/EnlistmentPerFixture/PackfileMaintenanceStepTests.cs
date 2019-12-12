using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class PackfileMaintenanceStepTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true to avoid any of the tests inadvertently corrupting
        // the cache
        public PackfileMaintenanceStepTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        private string GitObjectRoot => this.Enlistment.GetObjectRoot(this.fileSystem);
        private string PackRoot => this.Enlistment.GetPackRoot(this.fileSystem);

        [TestCase, Order(1)]
        public void ExpireClonePack()
        {
            this.GetPackSizes(out int packCount, out long maxSize, out long minSize, out long totalSize);

            // We should have at least two packs:
            //
            // 1. the pack-<hash>.pack from clone.
            // 2. a prefetch-<timestamp>-<hash>.pack from prefetch.
            //
            // The prefetch pack is newer, and covers all the objects in the clone pack,
            // so the clone pack will be expired when we run the step.

            Directory.GetFiles(this.PackRoot, "*.keep")
                .Count()
                .ShouldEqual(1);

            packCount.ShouldEqual(2, message: "Incorrect packfile layout for expire test");

            // Ensure we have a multi-pack-index (not created on clone)
            GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                $"multi-pack-index write --object-dir={this.GitObjectRoot}");

            this.Enlistment.PackfileMaintenanceStep();

            List<string> packs = this.GetPackfiles();

            packs.Count.ShouldEqual(1, $"incorrect number of packs after first step: {packs.Count}");

            Path.GetFileName(packs[0])
                .StartsWith("prefetch-")
                .ShouldBeTrue($"packsBetween[0] should start with 'prefetch-': {packs[0]}");
        }

        [TestCase, Order(2)]
        public void RepackAllToOnePack()
        {
            // Create new pack(s) by prefetching blobs for a folder.
            // This generates a number of packs, based on the processor number (for parallel downloads).
            this.Enlistment.Prefetch($"--folders {Path.Combine("GVFS", "GVFS")}");

            // Create a multi-pack-index that covers the prefetch packs
            // (The post-fetch job creates a multi-pack-index only after a --commits prefetch)
            GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                $"multi-pack-index write --object-dir={this.GitObjectRoot}");

            // Run the step to ensure we don't have any packs that will be expired during the repack step
            this.Enlistment.PackfileMaintenanceStep();

            this.GetPackSizes(out int afterPrefetchPackCount, out long maxSize, out long minSize, out long totalSize);

            // Cannot be sure of the count, as the prefetch uses parallel threads to get multiple packs
            afterPrefetchPackCount.ShouldBeAtLeast(2);

            this.Enlistment.PackfileMaintenanceStep(batchSize: totalSize - minSize + 1);
            this.GetPackSizes(out int packCount, out maxSize, out minSize, out totalSize);

            // We should not have expired any packs, but created a new one with repack
            packCount.ShouldEqual(afterPrefetchPackCount + 1, $"incorrect number of packs after repack step: {packCount}");
        }

        [TestCase, Order(3)]
        public void ExpireAllButOneAndKeep()
        {
            string prefetchPack = Directory.GetFiles(this.PackRoot, "prefetch-*.pack")
                                           .FirstOrDefault();

            prefetchPack.ShouldNotBeNull();

            // We should expire all packs except the one we just created,
            // and the prefetch pack which is marked as ".keep"
            this.Enlistment.PackfileMaintenanceStep();

            List<string> packsAfter = this.GetPackfiles();

            packsAfter.Count.ShouldEqual(2, $"incorrect number of packs after final expire step: {packsAfter.Count}");
            packsAfter.Contains(prefetchPack).ShouldBeTrue($"packsAfter does not contain prefetch pack ({prefetchPack})");
        }

        private List<string> GetPackfiles()
        {
            return Directory.GetFiles(this.PackRoot, "*.pack").ToList();
        }

        private void GetPackSizes(out int packCount, out long maxSize, out long minSize, out long totalSize)
        {
            totalSize = 0;
            maxSize = 0;
            minSize = long.MaxValue;
            packCount = 0;

            foreach (string file in this.GetPackfiles())
            {
                packCount++;
                long size = new FileInfo(Path.Combine(this.PackRoot, file)).Length;
                totalSize += size;

                if (size > maxSize)
                {
                    maxSize = size;
                }

                if (size < minSize)
                {
                    minSize = size;
                }
            }
        }
    }
}
