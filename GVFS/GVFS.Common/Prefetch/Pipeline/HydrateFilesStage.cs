using GVFS.Common.Prefetch.Git;
using GVFS.Common.Tracing;
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
            using (ITracer activity = this.tracer.StartActivity("ReadFiles", EventLevel.Informational))
            {
                int readFilesCurrentThread = 0;
                int failedFilesCurrentThread = 0;

                byte[] buffer = new byte[1];
                string blobId;
                while (this.availableBlobs.TryTake(out blobId, Timeout.Infinite))
                {
                    foreach (PathWithMode modeAndPath in this.blobIdToPaths[blobId])
                    {
                        bool succeeded = GVFSPlatform.Instance.FileSystem.HydrateFile(Path.Combine(this.workingDirectoryRoot, modeAndPath.Path), buffer);
                        if (succeeded)
                        {
                            Interlocked.Increment(ref this.readFileCount);
                            readFilesCurrentThread++;
                        }
                        else
                        {
                            activity.RelatedError("Failed to read " + modeAndPath.Path);

                            failedFilesCurrentThread++;
                            this.HasFailures = true;
                        }
                    }
                }

                activity.Stop(
                    new EventMetadata
                    {
                        { "FilesRead", readFilesCurrentThread },
                        { "Failures", failedFilesCurrentThread },
                    });
            }
        }
    }
}
