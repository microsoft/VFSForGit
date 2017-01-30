using GVFS.Common.Git;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

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

        public virtual string HeadTreeSha
        {
            get { return this.Context.Repository.GetHeadTreeSha(); }
        }

        protected GVFSContext Context { get; private set; }

        public virtual SafeHandle OpenGitObject(string firstTwoShaDigits, string remainingShaDigits)
        {
            return
                this.OpenLooseObject(this.objectsPath, firstTwoShaDigits, remainingShaDigits)
                ?? this.DownloadObject(firstTwoShaDigits, remainingShaDigits);
        }

        public bool TryCopyBlobContentStream(string sha, Action<StreamReader, long> writeAction)
        {
            if (!this.Context.Repository.TryCopyBlobContentStream(sha, writeAction))
            {
                if (!this.TryDownloadAndSaveObject(sha.Substring(0, 2), sha.Substring(2)))
                {
                    return false;
                }

                return this.Context.Repository.TryCopyBlobContentStream(sha, writeAction);
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

        public bool TryGetBlobSizeLocally(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength(sha, out length);
        }

        public List<HttpGitObjects.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds);
        }

        private SafeHandle OpenLooseObject(string objectsRoot, string firstTwoShaDigits, string remainingShaDigits)
        {
            string looseObjectPath = Path.Combine(
                objectsRoot,
                firstTwoShaDigits,
                remainingShaDigits);

            if (this.Context.FileSystem.FileExists(looseObjectPath))
            {
                return this.Context.FileSystem.OpenFile(looseObjectPath, FileMode.Open, (FileAccess)NativeMethods.FileAccess.FILE_GENERIC_READ, FileAttributes.Normal, FileShare.Read);
            }

            return null;
        }

        private SafeHandle DownloadObject(string firstTwoShaDigits, string remainingShaDigits)
        {
            this.TryDownloadAndSaveObject(firstTwoShaDigits, remainingShaDigits);
            return this.OpenLooseObject(this.objectsPath, firstTwoShaDigits, remainingShaDigits);
        }
    }
}
