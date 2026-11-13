using GVFS.Common.Prefetch;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class PrefetchProgressTests
    {
        [TestCase]
        public void ProgressIsSampledInsteadOfLoggedForEveryUpdate()
        {
            using MockTracer tracer = new MockTracer();
            PrefetchProgress progress = new PrefetchProgress(tracer);
            progress.SetPhase("Checking cached packs");
            progress.StartResponse(1);
            progress.StartPack(1, 1024);
            progress.StartIndex(1);
            for (int i = 0; i < 500; ++i)
            {
                progress.AddDownloadedBytes(1);
                progress.UpdateIndex(1, new string('x', 1000));
            }

            Assert.That(tracer.RelatedInfoEvents, Has.Count.EqualTo(1));
            Assert.That(tracer.RelatedInfoEvents[0], Does.StartWith("Prefetch progress: Checking cached packs; elapsed "));
            Assert.That(progress.GetMessage(), Does.Contain(new string('x', 160)).And.Not.Contain(new string('x', 161)));
        }

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

            Assert.That(progress.GetMessage(), Does.StartWith("Resolving deltas: 35% [pack 1]").And.Contain("indexing 2"));
            progress.CompleteIndex(2);
            progress.CompletePack();
            Assert.That(progress.GetMessage(), Does.Contain("indexes ready 1/43; indexing 1").And.Contain("[pack 1]"));

            progress.CompleteIndex(1);
            progress.CompletePack();
            progress.CompleteDownloads();
            Assert.That(progress.GetMessage(), Does.Contain("Downloaded 2/43 packs; indexes ready 2/43; finalizing packs"));
        }

        [TestCase]
        public void EmptyNativeFramesDoNotEraseTheLastProgress()
        {
            PrefetchProgress progress = new PrefetchProgress(null);
            progress.StartResponse(1);
            progress.StartIndex(1);
            progress.UpdateIndex(1, "Resolving deltas: 42%");
            progress.UpdateIndex(1, "\r\n\t ");
            Assert.That(progress.GetMessage(), Does.StartWith("Resolving deltas: 42% [pack 1]"));
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
