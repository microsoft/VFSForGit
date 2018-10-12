using GVFS.Common.Tracing;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Common.Prefetch.Jobs
{
    public class ReadFilesJob : Job
    {
        private readonly ConcurrentDictionary<string, HashSet<string>> blobIdToPaths;
        private readonly BlockingCollection<string> availableBlobs;

        private ITracer tracer;
        private int readFileCount;

        public ReadFilesJob(int maxThreads, ConcurrentDictionary<string, HashSet<string>> blobIdToPaths, BlockingCollection<string> availableBlobs, ITracer tracer)
            : base(maxThreads)
        {
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
                    foreach (string path in this.blobIdToPaths[blobId])
                    {
                        bool succeeded = GVFSPlatform.Instance.FileSystem.HydrateFile(path, buffer);
                        if (succeeded)
                        {
                            Interlocked.Increment(ref this.readFileCount);
                            readFilesCurrentThread++;
                        }
                        else
                        {
                            activity.RelatedError("Failed to read " + path);

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
