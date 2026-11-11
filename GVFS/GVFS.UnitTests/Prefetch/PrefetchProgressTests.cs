using GVFS.Common.Prefetch;
using NUnit.Framework;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class PrefetchProgressTests
    {
        [TestCase]
        public void DownloadsAndConcurrentIndexesRemainIndependent()
        {
            PrefetchProgress progress = new PrefetchProgress(null);
            progress.StartResponse(43);
            progress.StartPack(1, 1024L * 1024 * 1024);
            progress.AddDownloadedBytes(512L * 1024 * 1024);
            Assert.That(progress.GetMessage(), Does.Contain("Downloading pack 1/43: 512.0 MiB/1.0 GiB"));
            progress.CompletePackDownload();
            progress.StartIndex(1);
            progress.UpdateIndex(1, "Resolving deltas: 35%");
            progress.StartPack(2, 1024 * 1024);
            progress.CompletePackDownload();
            progress.StartIndex(2);
            progress.UpdateIndex(2, "Indexing objects: 99%");

            Assert.That(progress.GetMessage(), Does.Contain("indexing 2; pack 1: Resolving deltas: 35%"));
            progress.CompleteIndex(2);
            progress.CompletePack();
            Assert.That(progress.GetMessage(), Does.Contain("indexes ready 1/43; indexing 1; pack 1:"));

            progress.CompleteIndex(1);
            progress.CompletePack();
            progress.CompleteDownloads();
            Assert.That(progress.GetMessage(), Does.Contain("Downloaded 2/43 packs; indexes ready 2/43; finalizing packs"));
        }

        [TestCase]
        public void RetryResetsResponseCountersAndNativeProgressCannotInjectControlCharacters()
        {
            PrefetchProgress progress = new PrefetchProgress(null);
            progress.StartResponse(2);
            progress.StartIndex(1);
            progress.UpdateIndex(1, "\rIndexing objects:\n 10%\t");
            string message = progress.GetMessage();
            Assert.That(message, Does.Contain("Indexing objects: 10%"));
            Assert.That(message, Does.Not.Contain("\r").And.Not.Contain("\n").And.Not.Contain("\t"));
            progress.CompletePack();

            progress.StartResponse(1);
            progress.StartPack(1, 1024);
            Assert.That(progress.GetMessage(), Does.StartWith("Attempt 2; Downloading pack 1/1:"));
            Assert.That(progress.GetMessage(), Does.Contain("indexes ready 0/1").And.Not.Contain("Indexing objects:"));
        }
    }
}
