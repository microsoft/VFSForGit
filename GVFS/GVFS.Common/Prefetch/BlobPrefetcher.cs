using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Prefetch.Pipeline;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Prefetch
{
    public class BlobPrefetcher
    {
        protected const string RefsHeadsGitPath = "refs/heads/";

        protected readonly Enlistment Enlistment;
        protected readonly GitObjectsHttpRequestor ObjectRequestor;
        protected readonly GitObjects GitObjects;
        protected readonly ITracer Tracer;

        protected readonly int ChunkSize;
        protected readonly int SearchThreadCount;
        protected readonly int DownloadThreadCount;
        protected readonly int IndexThreadCount;

        protected readonly bool SkipConfigUpdate;

        private const string AreaPath = nameof(BlobPrefetcher);
        private static string pathSeparatorString = Path.DirectorySeparatorChar.ToString();

        public BlobPrefetcher(
            ITracer tracer,
            Enlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
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
            this.ObjectRequestor = objectRequestor;

            this.GitObjects = new PrefetchGitObjects(tracer, enlistment, this.ObjectRequestor);
            this.FileList = new List<string>();
            this.FolderList = new List<string>();

            // We never want to update config settings for a GVFSEnlistment
            this.SkipConfigUpdate = enlistment is GVFSEnlistment;
        }

        public bool HasFailures { get; protected set; }

        public List<string> FileList { get; }

        public List<string> FolderList { get; }

        public static bool TryLoadFolderList(Enlistment enlistment, string foldersInput, string folderListFile, List<string> folderListOutput, out string error)
        {
            folderListOutput.AddRange(
                foldersInput.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => BlobPrefetcher.ToAbsolutePath(enlistment, path, isFolder: true)));

            if (!string.IsNullOrWhiteSpace(folderListFile))
            {
                if (File.Exists(folderListFile))
                {
                    IEnumerable<string> allLines = File.ReadAllLines(folderListFile)
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .Where(line => !line.StartsWith(GVFSConstants.GitCommentSign.ToString()))
                        .Select(path => BlobPrefetcher.ToAbsolutePath(enlistment, path, isFolder: true));

                    folderListOutput.AddRange(allLines);
                }
                else
                {
                    error = string.Format("Could not find '{0}' for folder list.", folderListFile);
                    return false;
                }
            }

            folderListOutput.RemoveAll(string.IsNullOrWhiteSpace);

            foreach (string folder in folderListOutput)
            {
                if (folder.Contains("*"))
                {
                    error = "Wildcards are not supported for folders. Invalid entry: " + folder;
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static bool TryLoadFileList(Enlistment enlistment, string filesInput, List<string> fileListOutput, out string error)
        {
            fileListOutput.AddRange(
                filesInput.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => BlobPrefetcher.ToAbsolutePath(enlistment, path, isFolder: false)));

            foreach (string file in fileListOutput)
            {
                if (file.IndexOf('*', 1) != -1)
                {
                    error = "Only prefix wildcards are supported. Invalid entry: " + file;
                    return false;
                }

                if (file.EndsWith(GVFSConstants.GitPathSeparatorString) ||
                    file.EndsWith(pathSeparatorString))
                {
                    error = "Folders are not allowed in the file list. Invalid entry: " + file;
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static void AppendToNewlineSeparatedFile(string filename, string newContent)
        {
            AppendToNewlineSeparatedFile(new PhysicalFileSystem(), filename, newContent);
        }

        public static void AppendToNewlineSeparatedFile(PhysicalFileSystem fileSystem, string filename, string newContent)
        {
            using (Stream fileStream = fileSystem.OpenFileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, false))
            {
                using (StreamReader reader = new StreamReader(fileStream))
                using (StreamWriter writer = new StreamWriter(fileStream))
                {
                    long position = reader.BaseStream.Seek(0, SeekOrigin.End);
                    if (position > 0)
                    {
                        reader.BaseStream.Seek(position - 1, SeekOrigin.Begin);
                    }

                    string lastCharacter = reader.ReadToEnd();
                    if (lastCharacter != "\n" && lastCharacter != string.Empty)
                    {
                        writer.Write("\n");
                    }

                    writer.Write(newContent.Trim());
                    writer.Write("\n");
                }

                fileStream.Close();
            }
        }

        /// <param name="branchOrCommit">A specific branch to filter for, or null for all branches returned from info/refs</param>
        public virtual void Prefetch(string branchOrCommit, bool isBranch)
        {
            int matchedBlobCount;
            int downloadedBlobCount;
            int readFileCount;
            
            this.PrefetchWithStats(branchOrCommit, isBranch, false, out matchedBlobCount, out downloadedBlobCount, out readFileCount);
        }

        public void PrefetchWithStats(
            string branchOrCommit,
            bool isBranch,
            bool readFilesAfterDownload,
            out int matchedBlobCount,
            out int downloadedBlobCount,
            out int readFileCount)
        {
            matchedBlobCount = 0;
            downloadedBlobCount = 0;
            readFileCount = 0;

            if (string.IsNullOrWhiteSpace(branchOrCommit))
            {
                throw new FetchException("Must specify branch or commit to fetch");
            }

            GitRefs refs = null;
            string commitToFetch;
            if (isBranch)
            {
                refs = this.ObjectRequestor.QueryInfoRefs(branchOrCommit);
                if (refs == null)
                {
                    throw new FetchException("Could not query info/refs from: {0}", this.Enlistment.RepoUrl);
                }
                else if (refs.Count == 0)
                {
                    throw new FetchException("Could not find branch {0} in info/refs from: {1}", branchOrCommit, this.Enlistment.RepoUrl);
                }

                commitToFetch = refs.GetTipCommitId(branchOrCommit);
            }
            else
            {
                commitToFetch = branchOrCommit;
            }

            this.DownloadMissingCommit(commitToFetch, this.GitObjects);

            // Configure pipeline
            // LsTreeHelper output => FindMissingBlobs => BatchDownload => IndexPack
            string shallowFile = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Shallow);

            string previousCommit = null;

            // Use the shallow file to find a recent commit to diff against to try and reduce the number of SHAs to check.
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

            DiffHelper blobEnumerator = new DiffHelper(this.Tracer, this.Enlistment, this.FileList, this.FolderList, includeSymLinks: false);

            ThreadStart performDiff = () =>
            {
                blobEnumerator.PerformDiff(previousCommit, commitToFetch);
                this.HasFailures |= blobEnumerator.HasFailures;
            };

            if (readFilesAfterDownload)
            {
                // Call synchronously to ensure that blobEnumerator.FileAddOperations
                // is completely populated when fileHydrator starts
                performDiff();
            }
            else
            {
                new Thread(performDiff).Start();
            }

            BlockingCollection<string> availableBlobs = new BlockingCollection<string>();

            FindMissingBlobsStage blobFinder = new FindMissingBlobsStage(this.SearchThreadCount, blobEnumerator.RequiredBlobs, availableBlobs, this.Tracer, this.Enlistment);
            BatchObjectDownloadStage downloader = new BatchObjectDownloadStage(this.DownloadThreadCount, this.ChunkSize, blobFinder.MissingBlobs, availableBlobs, this.Tracer, this.Enlistment, this.ObjectRequestor, this.GitObjects);
            IndexPackStage packIndexer = new IndexPackStage(this.IndexThreadCount, downloader.AvailablePacks, availableBlobs, this.Tracer, this.GitObjects);
            HydrateFilesStage fileHydrator = new HydrateFilesStage(Environment.ProcessorCount * 2, blobEnumerator.FileAddOperations, availableBlobs, this.Tracer);
            
            blobFinder.Start();
            downloader.Start();

            if (readFilesAfterDownload)
            {
                fileHydrator.Start();
            }

            // If indexing happens during searching, searching progressively gets slower, so wait on searching before indexing.
            blobFinder.WaitForCompletion();
            this.HasFailures |= blobFinder.HasFailures;

            packIndexer.Start();

            downloader.WaitForCompletion();
            this.HasFailures |= downloader.HasFailures;

            packIndexer.WaitForCompletion();
            this.HasFailures |= packIndexer.HasFailures;

            availableBlobs.CompleteAdding();

            if (readFilesAfterDownload)
            {
                fileHydrator.WaitForCompletion();
                this.HasFailures |= fileHydrator.HasFailures;
            }

            matchedBlobCount = blobFinder.AvailableBlobCount + blobFinder.MissingBlobCount;
            downloadedBlobCount = blobFinder.MissingBlobCount;
            readFileCount = fileHydrator.ReadFileCount;

            if (!this.SkipConfigUpdate && !this.HasFailures)
            {
                this.UpdateRefs(branchOrCommit, isBranch, refs);

                if (isBranch)
                {
                    this.HasFailures |= !this.UpdateRefSpec(this.Tracer, this.Enlistment, branchOrCommit, refs);
                }
            }
        }

        protected bool UpdateRefSpec(ITracer tracer, Enlistment enlistment, string branchOrCommit, GitRefs refs)
        {
            using (ITracer activity = tracer.StartActivity("UpdateRefSpec", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                const string OriginRefMapSettingName = "remote.origin.fetch";

                // We must update the refspec to get proper "git pull" functionality.
                string localBranch = branchOrCommit.StartsWith(RefsHeadsGitPath) ? branchOrCommit : (RefsHeadsGitPath + branchOrCommit);
                string remoteBranch = refs.GetBranchRefPairs().Single().Key;
                string refSpec = "+" + localBranch + ":" + remoteBranch;

                GitProcess git = new GitProcess(enlistment);

                // Replace all ref-specs this
                // * ensures the default refspec (remote.origin.fetch=+refs/heads/*:refs/remotes/origin/*) is removed which avoids some "git fetch/pull" failures
                // * gives added "git fetch" performance since git will only fetch the branch provided in the refspec.
                GitProcess.Result setResult = git.SetInLocalConfig(OriginRefMapSettingName, refSpec, replaceAll: true);
                if (setResult.ExitCodeIsFailure)
                {
                    activity.RelatedError("Could not update ref spec to {0}: {1}", refSpec, setResult.Errors);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// * Updates any remote branch (N/A for fetch of detached commit)
        /// * Updates shallow file
        /// </summary>
        protected virtual void UpdateRefs(string branchOrCommit, bool isBranch, GitRefs refs)
        {
            string commitSha = null;
            if (isBranch)
            {
                KeyValuePair<string, string> remoteRef = refs.GetBranchRefPairs().Single();
                string remoteBranch = remoteRef.Key;
                commitSha = remoteRef.Value;

                this.HasFailures |= !this.UpdateRef(this.Tracer, remoteBranch, commitSha);
            }
            else
            {
                commitSha = branchOrCommit;
            }

            // Update shallow file to ensure this is a valid shallow repo
            AppendToNewlineSeparatedFile(Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Shallow), commitSha);
        }

        protected bool UpdateRef(ITracer tracer, string refName, string targetCommitish)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RefName", refName);
            metadata.Add("TargetCommitish", targetCommitish);
            using (ITracer activity = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata))
            {
                GitProcess gitProcess = new GitProcess(this.Enlistment);
                GitProcess.Result result = null;
                if (this.IsSymbolicRef(targetCommitish))
                {
                    // Using update-ref with a branch name will leave a SHA in the ref file which detaches HEAD, so use symbolic-ref instead.
                    result = gitProcess.UpdateBranchSymbolicRef(refName, targetCommitish);
                }
                else
                {
                    result = gitProcess.UpdateBranchSha(refName, targetCommitish);
                }

                if (result.ExitCodeIsFailure)
                {
                    activity.RelatedError(result.Errors);
                    return false;
                }

                return true;
            }
        }

        protected void DownloadMissingCommit(string commitSha, GitObjects gitObjects)
        {
            EventMetadata startMetadata = new EventMetadata();
            startMetadata.Add("CommitSha", commitSha);

            using (ITracer activity = this.Tracer.StartActivity("DownloadTrees", EventLevel.Informational, Keywords.Telemetry, startMetadata))
            {
                using (PrefetchLibGit2Repo repo = new PrefetchLibGit2Repo(this.Tracer, this.Enlistment.WorkingDirectoryRoot))
                {
                    if (!repo.ObjectExists(commitSha))
                    {
                        if (!gitObjects.TryDownloadCommit(commitSha))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("ObjectsEndpointUrl", this.ObjectRequestor.CacheServer.ObjectsEndpointUrl);
                            activity.RelatedError(metadata, "Could not download commits");
                            throw new FetchException("Could not download commits from {0}", this.ObjectRequestor.CacheServer.ObjectsEndpointUrl);
                        }
                    }
                }
            }
        }

        private static string ToAbsolutePath(Enlistment enlistment, string path, bool isFolder)
        {
            string absolute = 
                path.StartsWith("*")
                ? path
                : Path.Combine(enlistment.WorkingDirectoryRoot, path.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));

            if (isFolder &&
                !absolute.EndsWith(pathSeparatorString))
            {
                absolute += pathSeparatorString;
            }

            return absolute;
        }

        private bool IsSymbolicRef(string targetCommitish)
        {
            return targetCommitish.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
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