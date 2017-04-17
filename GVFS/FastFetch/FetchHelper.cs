using FastFetch.Git;
using FastFetch.Jobs;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FastFetch
{
    public class FetchHelper
    {
        protected readonly Enlistment Enlistment;
        protected readonly HttpGitObjects HttpGitObjects;
        protected readonly GitObjects GitObjects;
        protected readonly ITracer Tracer;

        protected readonly int ChunkSize;
        protected readonly int SearchThreadCount;
        protected readonly int DownloadThreadCount;
        protected readonly int IndexThreadCount;

        protected readonly bool SkipConfigUpdate;

        private const string AreaPath = nameof(FetchHelper);

        // Shallow clones don't require their parent commits
        private const int CommitDepth = 1;

        public FetchHelper(
            ITracer tracer,
            Enlistment enlistment,
            int chunkSize,
            int searchThreadCount,
            int downloadThreadCount,
            int indexThreadCount)
        {
            this.SearchThreadCount = searchThreadCount;
            this.DownloadThreadCount = downloadThreadCount;
            this.IndexThreadCount = indexThreadCount;
            this.ChunkSize = chunkSize;
            this.Tracer = tracer;
            this.Enlistment = enlistment;
            this.HttpGitObjects = new HttpGitObjects(tracer, enlistment, downloadThreadCount);
            this.GitObjects = new GitObjects(tracer, enlistment, this.HttpGitObjects);
            this.PathWhitelist = new List<string>();

            // We never want to update config settings for a GVFSEnlistment
            this.SkipConfigUpdate = enlistment is GVFSEnlistment;
        }

        public int MaxRetries
        {
            get { return this.HttpGitObjects.MaxRetries; }
            set { this.HttpGitObjects.MaxRetries = value; }
        }

        public bool HasFailures { get; protected set; }

        public List<string> PathWhitelist { get; private set; }

        public static bool TryLoadPathWhitelist(ITracer tracer, string pathWhitelistInput, string pathWhitelistFile, Enlistment enlistment, List<string> pathWhitelistOutput)
        {
            Func<string, string> makePathAbsolute = path => Path.Combine(enlistment.EnlistmentRoot, path.Replace('/', '\\').TrimStart('\\'));

            pathWhitelistOutput.AddRange(pathWhitelistInput.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(makePathAbsolute));

            if (!string.IsNullOrWhiteSpace(pathWhitelistFile))
            {
                if (File.Exists(pathWhitelistFile))
                {
                    IEnumerable<string> allLines = File.ReadAllLines(pathWhitelistFile)
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .Where(line => !line.StartsWith(GVFSConstants.GitCommentSignString))
                        .Select(makePathAbsolute);

                    pathWhitelistOutput.AddRange(allLines);
                }
                else
                {
                    tracer.RelatedError("Could not find '{0}' for folder filtering.", pathWhitelistFile);
                    Console.WriteLine("Could not find '{0}' for folder filtering.", pathWhitelistFile);
                    return false;
                }
            }

            pathWhitelistOutput.RemoveAll(string.IsNullOrWhiteSpace);
            return true;
        }

        /// <param name="branchOrCommit">A specific branch to filter for, or null for all branches returned from info/refs</param>
        public virtual void FastFetch(string branchOrCommit, bool isBranch)
        {
            if (string.IsNullOrWhiteSpace(branchOrCommit))
            {
                throw new FetchException("Must specify branch or commit to fetch");
            }

            GitRefs refs = null;
            string commitToFetch;
            if (isBranch)
            {
                refs = this.HttpGitObjects.QueryInfoRefs(branchOrCommit);
                if (refs == null)
                {
                    throw new FetchException("Could not query info/refs from: {0}", this.Enlistment.RepoUrl);
                }
                else if (refs.Count == 0)
                {
                    throw new FetchException("Could not find branch {0} in info/refs from: {1}", branchOrCommit, this.Enlistment.RepoUrl);
                }

                commitToFetch = refs.GetTipCommitIds().Single();
            }
            else
            {
                commitToFetch = branchOrCommit;
            }

            this.DownloadMissingCommit(commitToFetch, this.GitObjects);
            
            // Dummy output queue since we don't need to checkout available blobs
            BlockingCollection<string> availableBlobs = new BlockingCollection<string>();

            // Configure pipeline
            // LsTreeHelper output => FindMissingBlobs => BatchDownload => IndexPack
            string shallowFile = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Shallow);

            string previousCommit = null;
            
            // Use the shallow file to find a recent commit to diff against to try and reduce the number of SHAs to check
            DiffHelper blobEnumerator = new DiffHelper(this.Tracer, this.Enlistment, this.PathWhitelist);
            if (File.Exists(shallowFile))
            {
                previousCommit = File.ReadAllLines(shallowFile).Where(line => !string.IsNullOrWhiteSpace(line)).LastOrDefault();
                if (string.IsNullOrWhiteSpace(previousCommit))
                {
                    this.Tracer.RelatedError("Shallow file exists, but contains no valid SHAs.");
                    this.HasFailures = true;
                    return;
                }
            }

            blobEnumerator.PerformDiff(previousCommit, commitToFetch);
            this.HasFailures |= blobEnumerator.HasFailures;

            FindMissingBlobsJob blobFinder = new FindMissingBlobsJob(this.SearchThreadCount, blobEnumerator.RequiredBlobs, availableBlobs, this.Tracer, this.Enlistment);
            BatchObjectDownloadJob downloader = new BatchObjectDownloadJob(this.DownloadThreadCount, this.ChunkSize, blobFinder.DownloadQueue, availableBlobs, this.Tracer, this.Enlistment, this.HttpGitObjects, this.GitObjects);
            IndexPackJob packIndexer = new IndexPackJob(this.IndexThreadCount, downloader.AvailablePacks, availableBlobs, this.Tracer, this.GitObjects);
            
            blobFinder.Start();
            downloader.Start();
            
            // If indexing happens during searching, searching progressively gets slower, so wait on searching before indexing.
            blobFinder.WaitForCompletion();
            this.HasFailures |= blobFinder.HasFailures;

            // Index regardless of failures, it'll shorten the next fetch.
            packIndexer.Start();

            downloader.WaitForCompletion();
            this.HasFailures |= downloader.HasFailures;

            packIndexer.WaitForCompletion();
            this.HasFailures |= packIndexer.HasFailures;

            if (!this.SkipConfigUpdate && !this.HasFailures)
            {
                this.UpdateRefs(branchOrCommit, isBranch, refs);

                if (isBranch)
                {
                    this.HasFailures |= !RefSpecHelpers.UpdateRefSpec(this.Tracer, this.Enlistment, branchOrCommit, refs);
                }
            }
        }

        /// <summary>
        /// * Updates any remote branch (N/A for fetch of detached commit)
        /// * Updates shallow file
        /// </summary>
        protected virtual void UpdateRefs(string branchOrCommit, bool isBranch, GitRefs refs)
        {
            UpdateRefsHelper refHelper = new UpdateRefsHelper(this.Enlistment);
            string commitSha = null;
            if (isBranch)
            {
                KeyValuePair<string, string> remoteRef = refs.GetBranchRefPairs().Single();
                string remoteBranch = remoteRef.Key;
                commitSha = remoteRef.Value;

                this.HasFailures |= !refHelper.UpdateRef(this.Tracer, remoteBranch, commitSha);
            }
            else
            {
                commitSha = branchOrCommit;
            }

            // Update shallow file to ensure this is a valid shallow repo
            File.AppendAllText(Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Shallow), commitSha + "\n");
        }

        protected void DownloadMissingCommit(string commitSha, GitObjects gitObjects)
        {
            EventMetadata startMetadata = new EventMetadata();
            startMetadata.Add("CommitSha", commitSha);
            startMetadata.Add("CommitDepth", CommitDepth);

            using (ITracer activity = this.Tracer.StartActivity("DownloadTrees", EventLevel.Informational, startMetadata))
            {
                using (GitCatFileBatchCheckProcess catFileProcess = new GitCatFileBatchCheckProcess(this.Tracer, this.Enlistment))
                {
                    if (!catFileProcess.ObjectExists_CanTimeout(commitSha))
                    {
                        if (!gitObjects.TryDownloadAndSaveCommits(new[] { commitSha }, commitDepth: CommitDepth))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("ObjectsEndpointUrl", this.Enlistment.ObjectsEndpointUrl);
                            activity.RelatedError(metadata);
                            throw new FetchException("Could not download commits from {0}", this.Enlistment.ObjectsEndpointUrl);
                        }
                    }
                }
            }
        }

        public class FetchException : Exception
        {
            public FetchException(string format, params object[] args)
                : base(string.Format(format, args))
            {
            }
        }
    }
}