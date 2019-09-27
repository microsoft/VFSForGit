using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Prefetch.Pipeline;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
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

        private FileBasedDictionary<string, string> lastPrefetchArgs;

        public BlobPrefetcher(
            ITracer tracer,
            Enlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            int chunkSize,
            int searchThreadCount,
            int downloadThreadCount,
            int indexThreadCount)
            : this(tracer, enlistment, objectRequestor, null, null, null, chunkSize, searchThreadCount, downloadThreadCount, indexThreadCount)
        {
        }

        public BlobPrefetcher(
            ITracer tracer,
            Enlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            List<string> fileList,
            List<string> folderList,
            FileBasedDictionary<string, string> lastPrefetchArgs,
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
            this.FileList = fileList ?? new List<string>();
            this.FolderList = folderList ?? new List<string>();

            this.lastPrefetchArgs = lastPrefetchArgs;

            // We never want to update config settings for a GVFSEnlistment
            this.SkipConfigUpdate = enlistment is GVFSEnlistment;
        }

        public bool HasFailures { get; protected set; }

        public List<string> FileList { get; }

        public List<string> FolderList { get; }

        public static bool TryLoadFolderList(Enlistment enlistment, string foldersInput, string folderListFile, List<string> folderListOutput, bool readListFromStdIn, out string error)
        {
            return TryLoadFileOrFolderList(
                    enlistment,
                    foldersInput,
                    folderListFile,
                    isFolder: true,
                    readListFromStdIn: readListFromStdIn,
                    output: folderListOutput,
                    elementValidationFunction: s =>
                        s.Contains("*") ?
                            "Wildcards are not supported for folders. Invalid entry: " + s :
                            null,
                    error: out error);
        }

        public static bool TryLoadFileList(Enlistment enlistment, string filesInput, string filesListFile, List<string> fileListOutput, bool readListFromStdIn, out string error)
        {
            return TryLoadFileOrFolderList(
                enlistment,
                filesInput,
                filesListFile,
                readListFromStdIn: readListFromStdIn,
                isFolder: false,
                output: fileListOutput,
                elementValidationFunction: s =>
                {
                    if (s.IndexOf('*', 1) != -1)
                    {
                        return "Only prefix wildcards are supported. Invalid entry: " + s;
                    }

                    if (s.EndsWith(GVFSConstants.GitPathSeparatorString) ||
                        s.EndsWith(pathSeparatorString))
                    {
                        return "Folders are not allowed in the file list. Invalid entry: " + s;
                    }

                    return null;
                },
                error: out error);
        }

        public static bool IsNoopPrefetch(
            ITracer tracer,
            FileBasedDictionary<string, string> lastPrefetchArgs,
            string commitId,
            List<string> files,
            List<string> folders,
            bool hydrateFilesAfterDownload)
        {
            if (lastPrefetchArgs != null &&
                lastPrefetchArgs.TryGetValue(PrefetchArgs.CommitId, out string lastCommitId) &&
                lastPrefetchArgs.TryGetValue(PrefetchArgs.Files, out string lastFilesString) &&
                lastPrefetchArgs.TryGetValue(PrefetchArgs.Folders, out string lastFoldersString) &&
                lastPrefetchArgs.TryGetValue(PrefetchArgs.Hydrate, out string lastHydrateString))
            {
                string newFilesString = JsonConvert.SerializeObject(files);
                string newFoldersString = JsonConvert.SerializeObject(folders);
                bool isNoop =
                    commitId == lastCommitId &&
                    hydrateFilesAfterDownload.ToString() == lastHydrateString &&
                    newFilesString == lastFilesString &&
                    newFoldersString == lastFoldersString;

                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "BlobPrefetcher.IsNoopPrefetch",
                    new EventMetadata
                    {
                        { "Last" + PrefetchArgs.CommitId, lastCommitId },
                        { "Last" + PrefetchArgs.Files, lastFilesString },
                        { "Last" + PrefetchArgs.Folders, lastFoldersString },
                        { "Last" + PrefetchArgs.Hydrate, lastHydrateString },
                        { "New" + PrefetchArgs.CommitId, commitId },
                        { "New" + PrefetchArgs.Files, newFilesString },
                        { "New" + PrefetchArgs.Folders, newFoldersString },
                        { "New" + PrefetchArgs.Hydrate, hydrateFilesAfterDownload.ToString() },
                        { "Result", isNoop },
                    });

                return isNoop;
            }

            return false;
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
            int hydratedFileCount;

            this.PrefetchWithStats(branchOrCommit, isBranch, false, out matchedBlobCount, out downloadedBlobCount, out hydratedFileCount);
        }

        public void PrefetchWithStats(
            string branchOrCommit,
            bool isBranch,
            bool hydrateFilesAfterDownload,
            out int matchedBlobCount,
            out int downloadedBlobCount,
            out int hydratedFileCount)
        {
            matchedBlobCount = 0;
            downloadedBlobCount = 0;
            hydratedFileCount = 0;

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

            // For FastFetch only, examine the shallow file to determine the previous commit that had been fetched
            string shallowFile = Path.Combine(this.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Shallow);
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

            BlockingCollection<string> availableBlobs = new BlockingCollection<string>();

            ////
            // First create the pipeline
            //
            //  diff ---> blobFinder ---> downloader ---> packIndexer
            //    |           |              |                 |
            //     ------------------------------------------------------> fileHydrator
            ////

            // diff
            //  Inputs:
            //      * files/folders
            //      * commit id
            //  Outputs:
            //      * RequiredBlobs (property): Blob ids required to satisfy desired paths
            //      * FileAddOperations (property): Repo-relative paths corresponding to those blob ids
            DiffHelper diff = new DiffHelper(this.Tracer, this.Enlistment, this.FileList, this.FolderList, includeSymLinks: false);

            // blobFinder
            //  Inputs:
            //      * requiredBlobs (in param): Blob ids from output of `diff`
            //  Outputs:
            //      * availableBlobs (out param): Locally available blob ids (shared between `blobFinder`, `downloader`, and `packIndexer`, all add blob ids to the list as they are locally available)
            //      * MissingBlobs (property): Blob ids that are missing and need to be downloaded
            //      * AvailableBlobs (property): Same as availableBlobs
            FindBlobsStage blobFinder = new FindBlobsStage(this.SearchThreadCount, diff.RequiredBlobs, availableBlobs, this.Tracer, this.Enlistment);

            // downloader
            //  Inputs:
            //      * missingBlobs (in param): Blob ids from output of `blobFinder`
            //  Outputs:
            //      * availableBlobs (out param): Loose objects that have completed downloading (shared between `blobFinder`, `downloader`, and `packIndexer`, all add blob ids to the list as they are locally available)
            //      * AvailableObjects (property): Same as availableBlobs
            //      * AvailablePacks (property): Packfiles that have completed downloading
            BatchObjectDownloadStage downloader = new BatchObjectDownloadStage(this.DownloadThreadCount, this.ChunkSize, blobFinder.MissingBlobs, availableBlobs, this.Tracer, this.Enlistment, this.ObjectRequestor, this.GitObjects);

            // packIndexer
            //  Inputs:
            //      * availablePacks (in param): Packfiles that have completed downloading from output of `downloader`
            //  Outputs:
            //      * availableBlobs (out param): Blobs that have completed downloading and indexing (shared between `blobFinder`, `downloader`, and `packIndexer`, all add blob ids to the list as they are locally available)
            IndexPackStage packIndexer = new IndexPackStage(this.IndexThreadCount, downloader.AvailablePacks, availableBlobs, this.Tracer, this.GitObjects);

            // fileHydrator
            //  Inputs:
            //      * workingDirectoryRoot (in param): the root of the working directory where hydration takes place
            //      * blobIdsToPaths (in param): paths of all blob ids that need to be hydrated from output of `diff`
            //      * availableBlobs (in param): blobs id that are available locally, from whatever source
            //  Outputs:
            //      * Hydrated files on disk.
            HydrateFilesStage fileHydrator = new HydrateFilesStage(Environment.ProcessorCount * 2, this.Enlistment.WorkingDirectoryRoot, diff.FileAddOperations, availableBlobs, this.Tracer);

            // All the stages of the pipeline are created and wired up, now kick them off in the proper sequence

            ThreadStart performDiff = () =>
            {
                diff.PerformDiff(previousCommit, commitToFetch);
                this.HasFailures |= diff.HasFailures;
            };

            if (hydrateFilesAfterDownload)
            {
                // Call synchronously to ensure that diff.FileAddOperations
                // is completely populated when fileHydrator starts
                performDiff();
            }
            else
            {
                new Thread(performDiff).Start();
            }

            blobFinder.Start();
            downloader.Start();

            if (hydrateFilesAfterDownload)
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

            if (hydrateFilesAfterDownload)
            {
                fileHydrator.WaitForCompletion();
                this.HasFailures |= fileHydrator.HasFailures;
            }

            matchedBlobCount = blobFinder.AvailableBlobCount + blobFinder.MissingBlobCount;
            downloadedBlobCount = blobFinder.MissingBlobCount;
            hydratedFileCount = fileHydrator.ReadFileCount;

            if (!this.SkipConfigUpdate && !this.HasFailures)
            {
                this.UpdateRefs(branchOrCommit, isBranch, refs);

                if (isBranch)
                {
                    this.HasFailures |= !this.UpdateRefSpec(this.Tracer, this.Enlistment, branchOrCommit, refs);
                }
            }

            if (!this.HasFailures)
            {
                this.SavePrefetchArgs(commitToFetch, hydrateFilesAfterDownload);
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
            AppendToNewlineSeparatedFile(Path.Combine(this.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Shallow), commitSha);
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
                using (LibGit2Repo repo = new LibGit2Repo(this.Tracer, this.Enlistment.WorkingDirectoryBackingRoot))
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

        private static IEnumerable<string> GetFilesFromVerbParameter(string valueString)
        {
            return valueString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static IEnumerable<string> GetFilesFromFile(string fileName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Enumerable.Empty<string>();
            }

            if (!File.Exists(fileName))
            {
                error = string.Format("Could not find '{0}' list file.", fileName);
                return Enumerable.Empty<string>();
            }

            return File.ReadAllLines(fileName)
                        .Select(line => line.Trim());
        }

        private static IEnumerable<string> GetFilesFromStdin(bool shouldRead)
        {
            if (!shouldRead)
            {
                yield break;
            }

            string line;
            while ((line = Console.In.ReadLine()) != null)
            {
                yield return line.Trim();
            }
        }

        private static bool TryLoadFileOrFolderList(Enlistment enlistment, string valueString, string listFileName, bool readListFromStdIn, bool isFolder, List<string> output, Func<string, string> elementValidationFunction, out string error)
        {
            output.AddRange(
                GetFilesFromVerbParameter(valueString)
                .Union(GetFilesFromFile(listFileName, out string fileReadError))
                .Union(GetFilesFromStdin(readListFromStdIn))
                .Where(path => !path.StartsWith(GVFSConstants.GitCommentSign.ToString()))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => BlobPrefetcher.ToFilterPath(path, isFolder: isFolder)));

            if (!string.IsNullOrWhiteSpace(fileReadError))
            {
                error = fileReadError;
                return false;
            }

            string[] errorArray = output
                .Select(elementValidationFunction)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (errorArray != null && errorArray.Length > 0)
            {
                error = string.Join("\n", errorArray);
                return false;
            }

            error = null;
            return true;
        }

        private static string ToFilterPath(string path, bool isFolder)
        {
            string filterPath =
                path.StartsWith("*")
                ? path
                : path.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

            if (isFolder && filterPath.Length > 0 && !filterPath.EndsWith(pathSeparatorString))
            {
                filterPath += pathSeparatorString;
            }

            return filterPath;
        }

        private bool IsSymbolicRef(string targetCommitish)
        {
            return targetCommitish.StartsWith("refs/", GVFSPlatform.Instance.Constants.PathComparison);
        }

        private void SavePrefetchArgs(string targetCommit, bool hydrate)
        {
            if (this.lastPrefetchArgs != null)
            {
                this.lastPrefetchArgs.SetValuesAndFlush(
                    new[]
                    {
                        new KeyValuePair<string, string>(PrefetchArgs.CommitId, targetCommit),
                        new KeyValuePair<string, string>(PrefetchArgs.Files, JsonConvert.SerializeObject(this.FileList)),
                        new KeyValuePair<string, string>(PrefetchArgs.Folders, JsonConvert.SerializeObject(this.FolderList)),
                        new KeyValuePair<string, string>(PrefetchArgs.Hydrate, hydrate.ToString()),
                    });
            }
        }

        public class FetchException : Exception
        {
            public FetchException(string format, params object[] args)
                : base(string.Format(format, args))
            {
            }
        }

        private static class PrefetchArgs
        {
            public const string CommitId = "CommitId";
            public const string Files = "Files";
            public const string Folders = "Folders";
            public const string Hydrate = "Hydrate";
        }
    }
}
