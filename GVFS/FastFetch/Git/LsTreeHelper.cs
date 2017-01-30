using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FastFetch.Jobs
{
    public class LsTreeHelper
    {
        private const string AreaPath = nameof(LsTreeHelper);

        private List<string> pathWhitelist;
        private ITracer tracer;
        private Enlistment enlistment;

        public LsTreeHelper(
            IEnumerable<string> pathWhitelist,
            ITracer tracer,
            Enlistment enlistment)
        {
            this.pathWhitelist = new List<string>(pathWhitelist);
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.BlobIdOutput = new BlockingCollection<string>();
        }

        public BlockingCollection<string> BlobIdOutput { get; set; }

        public bool EnqueueAllBlobs(string rootTreeSha)
        {
            GitProcess git = new GitProcess(this.enlistment);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("TreeSha", rootTreeSha);
            using (ITracer activity = this.tracer.StartActivity(AreaPath, EventLevel.Informational, metadata))
            {
                GitProcess.Result result = git.LsTree(rootTreeSha, this.AddIfLineIsBlob, recursive: true);
                if (result.HasErrors)
                {
                    metadata.Add("ErrorMessage", result.Errors);
                    activity.RelatedError(metadata);
                    return false;
                }
            }

            this.BlobIdOutput.CompleteAdding();

            return true;
        }

        private void AddIfLineIsBlob(string blobLine)
        {
            int blobIdIndex = blobLine.IndexOf(GitCatFileProcess.BlobMarker);
            if (blobIdIndex > -1)
            {
                string blobSha = blobLine.Substring(blobIdIndex + GitCatFileProcess.TreeMarker.Length, GVFSConstants.ShaStringLength);
                string blobName = blobLine.Substring(blobLine.LastIndexOf('\t')).Trim();

                if (!this.pathWhitelist.Any() ||
                    this.pathWhitelist.Any(whitePath => blobName.StartsWith(whitePath, StringComparison.OrdinalIgnoreCase)))
                {
                    this.BlobIdOutput.Add(blobSha);
                }
            }
        }
    }
}
