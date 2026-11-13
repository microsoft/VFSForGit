using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Prefetch
{
    public class PrefetchProgress
    {
        private readonly Lock progressLock = new Lock();
        private readonly SortedDictionary<int, string> indexers = new SortedDictionary<int, string>();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly ITracer tracer;
        private TimeSpan lastLogTime = TimeSpan.FromSeconds(-30);
        private string phase = "Waiting for prefetch lock";
        private int attempt;
        private int packCount;
        private int currentPack;
        private int downloadedPacks;
        private int readyPacks;
        private long downloadedBytes;
        private long packBytes;
        private bool downloadsComplete;

        public PrefetchProgress(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public void SetPhase(string phase)
        {
            lock (this.progressLock)
            {
                this.phase = phase;
                this.LogProgress();
            }
        }

        public void StartResponse(int packCount)
        {
            lock (this.progressLock)
            {
                ++this.attempt;
                this.packCount = packCount;
                this.currentPack = 0;
                this.downloadedPacks = 0;
                this.readyPacks = 0;
                this.downloadedBytes = 0;
                this.packBytes = 0;
                this.downloadsComplete = false;
                this.indexers.Clear();
                this.phase = null;
                this.LogProgress();
            }
        }

        public void StartPack(int packNumber, long packBytes)
        {
            lock (this.progressLock)
            {
                this.currentPack = packNumber;
                this.packBytes = packBytes;
                this.downloadedBytes = 0;
                this.LogProgress();
            }
        }

        public void AddDownloadedBytes(long count)
        {
            lock (this.progressLock)
            {
                this.downloadedBytes += count;
                this.LogProgress();
            }
        }

        public void CompletePackDownload()
        {
            lock (this.progressLock)
            {
                ++this.downloadedPacks;
                this.LogProgress();
            }
        }

        public void CompleteDownloads()
        {
            lock (this.progressLock)
            {
                this.downloadsComplete = true;
                this.LogProgress();
            }
        }

        public void StartIndex(int packNumber)
        {
            lock (this.progressLock)
            {
                this.indexers.Add(packNumber, null);
                this.LogProgress();
            }
        }

        public void UpdateIndex(int packNumber, string message)
        {
            // Strip C0/C1 controls before native status text reaches the pipe, console, or log.
            string status = new string(message.Where(c => !char.IsControl(c)).Take(160).ToArray()).Trim();
            if (status.Length == 0)
            {
                return;
            }

            lock (this.progressLock)
            {
                this.indexers[packNumber] = status;
                this.LogProgress();
            }
        }

        public void CompleteIndex(int packNumber)
        {
            lock (this.progressLock)
            {
                this.indexers.Remove(packNumber);
                this.LogProgress();
            }
        }

        public void CompletePack()
        {
            lock (this.progressLock)
            {
                ++this.readyPacks;
                this.LogProgress();
            }
        }

        public string GetMessage()
        {
            lock (this.progressLock)
            {
                return this.GetMessageCore();
            }
        }

        private static string FormatBytes(long bytes)
        {
            return bytes >= 1024L * 1024 * 1024
                ? (bytes / (1024.0 * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " GiB"
                : (bytes / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";
        }

        private string GetMessageCore()
        {
            string status = this.phase;
            if (status == null)
            {
                status = this.downloadsComplete
                    ? $"Downloaded {this.downloadedPacks}/{this.packCount} packs"
                    : $"Downloading pack {this.currentPack}/{this.packCount}: {FormatBytes(this.downloadedBytes)}/{FormatBytes(this.packBytes)}";
                status += $"; indexes ready {this.readyPacks}/{this.packCount}";
                if (this.indexers.Count > 0)
                {
                    KeyValuePair<int, string> oldest = this.indexers.First();
                    status = $"{oldest.Value ?? "Starting index"} [pack {oldest.Key}]; " +
                        status + $"; indexing {this.indexers.Count}";
                }
                else if (this.downloadsComplete)
                {
                    status += "; finalizing packs";
                }

                if (this.attempt > 1)
                {
                    status = $"Attempt {this.attempt}; " + status;
                }
            }

            return status + "; elapsed " + this.elapsed.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private void LogProgress()
        {
            if (this.tracer != null && this.elapsed.Elapsed - this.lastLogTime >= TimeSpan.FromSeconds(30))
            {
                this.lastLogTime = this.elapsed.Elapsed;
                this.tracer.RelatedInfo("Prefetch progress: {0}", this.GetMessageCore());
            }
        }
    }
}
