using GVFS.Common.Git;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.Common.Physical.Git
{
    public class GVFSGitObjects : GitObjects
    {
        private static readonly TimeSpan NegativeCacheTTL = TimeSpan.FromSeconds(30);

        private string objectsPath;
        private ConcurrentDictionary<string, DateTime> objectNegativeCache;

        public GVFSGitObjects(GVFSContext context, HttpGitObjects httpGitObjects)
            : base(context.Tracer, context.Enlistment, httpGitObjects)
        {
            this.Context = context;
            this.objectsPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Root);

            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        protected GVFSContext Context { get; private set; }

        public bool TryCopyBlobContentStream_CanTimeout(string sha, Action<StreamReader, long> writeAction)
        {
            if (!this.Context.Repository.TryCopyBlobContentStream_CanTimeout(sha, writeAction))
            {
                if (!this.TryDownloadAndSaveObject(sha.Substring(0, 2), sha.Substring(2)))
                {
                    return false;
                }

                if (!this.Context.Repository.TryCopyBlobContentStream_CanTimeout(sha, writeAction))
                {
                    this.Tracer.RelatedError("Failed to cat-file after download. Trying again: " + sha);

                    // Due to a potential race, git sometimes fail to read the blob even though we just wrote it out.
                    // Retrying the read fixes that issue.
                    Thread.Sleep(100);
                    if (!this.Context.Repository.TryCopyBlobContentStream_CanTimeout(sha, writeAction))
                    {
                        this.Tracer.RelatedError("Failed to cat-file after multiple attempts: " + sha);
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TryDownloadAndSaveObject(string firstTwoShaDigits, string remainingShaDigits)
        {
            DateTime negativeCacheRequestTime;
            string objectId = firstTwoShaDigits + remainingShaDigits;

            if (this.objectNegativeCache.TryGetValue(objectId, out negativeCacheRequestTime))
            {
                if (negativeCacheRequestTime > DateTime.Now.Subtract(NegativeCacheTTL))
                {
                    return false;
                }

                this.objectNegativeCache.TryRemove(objectId, out negativeCacheRequestTime);
            }

            DownloadAndSaveObjectResult result = this.TryDownloadAndSaveObject(objectId);

            switch (result)
            {
                case DownloadAndSaveObjectResult.Success:
                    return true;
                case DownloadAndSaveObjectResult.ObjectNotOnServer:
                    this.objectNegativeCache.AddOrUpdate(objectId, DateTime.Now, (unused1, unused2) => DateTime.Now);
                    return false;
                case DownloadAndSaveObjectResult.Error:
                    return false;
                default:
                    throw new InvalidOperationException("Unknown DownloadAndSaveObjectResult value");
            }
        }

        public bool TryGetBlobSizeLocally_CanTimeout(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength_CanTimeout(sha, out length);
        }

        public List<HttpGitObjects.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds);
        }
    }
}
