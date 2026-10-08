using GVFS.Common.FileSystem;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GVFS.Common.Prefetch.Pipeline
{
    public class HydrateFilesStage : PrefetchPipelineStage
    {
        private readonly string workingDirectoryRoot;
        private readonly ConcurrentDictionary<string, HashSet<PathWithMode>> blobIdToPaths;
        private readonly BlockingCollection<string> availableBlobs;

        private ITracer tracer;
        private int readFileCount;

        public HydrateFilesStage(int maxThreads, string workingDirectoryRoot, ConcurrentDictionary<string, HashSet<PathWithMode>> blobIdToPaths, BlockingCollection<string> availableBlobs, ITracer tracer)
            : base(maxThreads)
        {
            this.workingDirectoryRoot = workingDirectoryRoot;
            this.blobIdToPaths = blobIdToPaths;
            this.availableBlobs = availableBlobs;

            this.tracer = tracer;
        }

        public int ReadFileCount
        {
            get { return this.readFileCount; }
        }

        protected override void DoWork()
        {
            // Keywords.Telemetry on the Stop event is required so the FailureSignatureCounts
            // summary (added below) actually reaches the telemetry pipe — without it, the
            // activity's Stop event is filtered out before it gets there.
            using (ITracer activity = this.tracer.StartActivity("ReadFiles", EventLevel.Informational, Keywords.Telemetry, null))
            {
                int readFilesCurrentThread = 0;
                int failedFilesCurrentThread = 0;

                // Keyed by HydrationFailureDiagnostics.GetFailureSignature(...) so a large failed
                // prefetch reports distinct failure kinds once in the summary, rather than only via
                // the (already per-file) "Failed to read" events below.
                Dictionary<string, int> failureSignatureCounts = null;

                byte[] buffer = new byte[1];
                string blobId;
                while (this.availableBlobs.TryTake(out blobId, Timeout.Infinite))
                {
                    foreach (PathWithMode modeAndPath in this.blobIdToPaths[blobId])
                    {
                        bool succeeded = GVFSPlatform.Instance.FileSystem.HydrateFile(Path.Combine(this.workingDirectoryRoot, modeAndPath.Path), buffer, out Exception failure);
                        if (succeeded)
                        {
                            Interlocked.Increment(ref this.readFileCount);
                            readFilesCurrentThread++;
                        }
                        else
                        {
                            EventMetadata metadata = HydrationFailureDiagnostics.BuildMetadata(modeAndPath.Path, failure);
                            activity.RelatedError(metadata, "Failed to read " + modeAndPath.Path);

                            failedFilesCurrentThread++;
                            this.HasFailures = true;

                            failureSignatureCounts ??= new Dictionary<string, int>();
                            string signature = HydrationFailureDiagnostics.GetFailureSignature(failure);
                            failureSignatureCounts.TryGetValue(signature, out int signatureCount);
                            failureSignatureCounts[signature] = signatureCount + 1;
                        }
                    }
                }

                EventMetadata stopMetadata = new EventMetadata
                {
                    { "FilesRead", readFilesCurrentThread },
                    { "Failures", failedFilesCurrentThread },
                };

                if (failureSignatureCounts != null)
                {
                    stopMetadata.Add("FailureSignatureCounts", HydrationFailureDiagnostics.FormatSignatureCounts(failureSignatureCounts));
                }

                activity.Stop(stopMetadata);
            }
        }
    }
}
