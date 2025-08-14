using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Maintenance;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.PlatformLoader;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static GVFS.Common.Git.LibGit2Repo;

namespace GVFS.Mount
{
    public class InProcessMount
    {
        // Tests show that 250 is the max supported pipe name length
        private const int MaxPipeNameLength = 250;
        private const int MutexMaxWaitTimeMS = 500;

        private readonly bool showDebugWindow;

        private FileSystemCallbacks fileSystemCallbacks;
        private GVFSDatabase gvfsDatabase;
        private GVFSEnlistment enlistment;
        private ITracer tracer;
        private GitMaintenanceScheduler maintenanceScheduler;

        private CacheServerInfo cacheServer;
        private RetryConfig retryConfig;
        private GitStatusCacheConfig gitStatusCacheConfig;

        private GVFSContext context;
        private GVFSGitObjects gitObjects;

        private MountState currentState;
        private HeartbeatThread heartbeat;
        private ManualResetEvent unmountEvent;

        private readonly Dictionary<string, string> treesWithDownloadedCommits = new Dictionary<string,string>();
        private DateTime lastCommitPackDownloadTime = DateTime.MinValue;

        // True if InProcessMount is calling git reset as part of processing
        // a folder dehydrate request
        private volatile bool resetForDehydrateInProgress;

        public InProcessMount(ITracer tracer, GVFSEnlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig, GitStatusCacheConfig gitStatusCacheConfig, bool showDebugWindow)
        {
            this.tracer = tracer;
            this.retryConfig = retryConfig;
            this.gitStatusCacheConfig = gitStatusCacheConfig;
            this.cacheServer = cacheServer;
            this.enlistment = enlistment;
            this.showDebugWindow = showDebugWindow;
            this.unmountEvent = new ManualResetEvent(false);
        }

        private enum MountState
        {
            Invalid = 0,

            Mounting,
            Ready,
            Unmounting,
            MountFailed
        }

        public void Mount(EventLevel verbosity, Keywords keywords)
        {
            this.currentState = MountState.Mounting;

            // We must initialize repo metadata before starting the pipe server so it
            // can immediately handle status requests
            string error;
            if (!RepoMetadata.TryInitialize(this.tracer, this.enlistment.DotGVFSRoot, out error))
            {
                this.FailMountAndExit("Failed to load repo metadata: " + error);
            }

            string gitObjectsRoot;
            if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
            {
                this.FailMountAndExit("Failed to determine git objects root from repo metadata: " + error);
            }

            string localCacheRoot;
            if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
            {
                this.FailMountAndExit("Failed to determine local cache path from repo metadata: " + error);
            }

            string blobSizesRoot;
            if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out blobSizesRoot, out error))
            {
                this.FailMountAndExit("Failed to determine blob sizes root from repo metadata: " + error);
            }

            this.tracer.RelatedEvent(
                EventLevel.Informational,
                "CachePathsLoaded",
                new EventMetadata
                {
                    { "gitObjectsRoot", gitObjectsRoot },
                    { "localCacheRoot", localCacheRoot },
                    { "blobSizesRoot", blobSizesRoot },
                });

            this.enlistment.InitializeCachePaths(localCacheRoot, gitObjectsRoot, blobSizesRoot);

            using (NamedPipeServer pipeServer = this.StartNamedPipe())
            {
                this.tracer.RelatedEvent(
                    EventLevel.Informational,
                    $"{nameof(this.Mount)}_StartedNamedPipe",
                    new EventMetadata { { "NamedPipeName", this.enlistment.NamedPipeName } });

                this.context = this.CreateContext();

                if (this.context.Unattended)
                {
                    this.tracer.RelatedEvent(EventLevel.Critical, GVFSConstants.UnattendedEnvironmentVariable, null);
                }

                this.ValidateMountPoints();

                string errorMessage;
                if (!HooksInstaller.TryUpdateHooks(this.context, out errorMessage))
                {
                    this.FailMountAndExit(errorMessage);
                }

                GVFSPlatform.Instance.ConfigureVisualStudio(this.enlistment.GitBinPath, this.tracer);

                this.MountAndStartWorkingDirectoryCallbacks(this.cacheServer);

                Console.Title = "GVFS " + ProcessHelper.GetCurrentProcessVersion() + " - " + this.enlistment.EnlistmentRoot;

                this.tracer.RelatedEvent(
                    EventLevel.Informational,
                    "Mount",
                    new EventMetadata
                    {
                        // Use TracingConstants.MessageKey.InfoMessage rather than TracingConstants.MessageKey.CriticalMessage
                        // as this message should not appear as an error
                        { TracingConstants.MessageKey.InfoMessage, "Virtual repo is ready" },
                    },
                    Keywords.Telemetry);

                this.currentState = MountState.Ready;

                this.unmountEvent.WaitOne();
            }
        }

        private GVFSContext CreateContext()
        {
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = this.CreateOrReportAndExit(
                () => new GitRepo(
                    this.tracer,
                    this.enlistment,
                    fileSystem),
                "Failed to read git repo");
            return new GVFSContext(this.tracer, fileSystem, gitRepo, this.enlistment);
        }

        private void ValidateMountPoints()
        {
            DirectoryInfo workingDirectoryRootInfo = new DirectoryInfo(this.enlistment.WorkingDirectoryBackingRoot);
            if (!workingDirectoryRootInfo.Exists)
            {
                this.FailMountAndExit("Failed to initialize file system callbacks. Directory \"{0}\" must exist.", this.enlistment.WorkingDirectoryBackingRoot);
            }

            string dotGitPath = Path.Combine(this.enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Root);
            DirectoryInfo dotGitPathInfo = new DirectoryInfo(dotGitPath);
            if (!dotGitPathInfo.Exists)
            {
                this.FailMountAndExit("Failed to mount. Directory \"{0}\" must exist.", dotGitPathInfo);
            }
        }

        private NamedPipeServer StartNamedPipe()
        {
            try
            {
                return NamedPipeServer.StartNewServer(this.enlistment.NamedPipeName, this.tracer, this.HandleRequest);
            }
            catch (PipeNameLengthException)
            {
                this.FailMountAndExit("Failed to create mount point. Mount path exceeds the maximum number of allowed characters");
                return null;
            }
        }

        private void FailMountAndExit(string error, params object[] args)
        {
            this.currentState = MountState.MountFailed;

            this.tracer.RelatedError(error, args);
            if (this.showDebugWindow)
            {
                Console.WriteLine("\nPress Enter to Exit");
                Console.ReadLine();
            }

            if (this.fileSystemCallbacks != null)
            {
                this.fileSystemCallbacks.Dispose();
                this.fileSystemCallbacks = null;
            }

            Environment.Exit((int)ReturnCode.GenericError);
        }

        private T CreateOrReportAndExit<T>(Func<T> factory, string reportMessage)
        {
            try
            {
                return factory();
            }
            catch (Exception e)
            {
                this.FailMountAndExit(reportMessage + " " + e.ToString());
                throw;
            }
        }

        private void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);

            switch (message.Header)
            {
                case NamedPipeMessages.GetStatus.Request:
                    this.HandleGetStatusRequest(connection);
                    break;

                case NamedPipeMessages.Unmount.Request:
                    this.HandleUnmountRequest(connection);
                    break;

                case NamedPipeMessages.AcquireLock.AcquireRequest:
                    this.HandleLockRequest(message.Body, connection);
                    break;

                case NamedPipeMessages.ReleaseLock.Request:
                    this.HandleReleaseLockRequest(message.Body, connection);
                    break;

                case NamedPipeMessages.DownloadObject.DownloadRequest:
                    this.HandleDownloadObjectRequest(message, connection);
                    break;

                case NamedPipeMessages.ModifiedPaths.ListRequest:
                    this.HandleModifiedPathsListRequest(message, connection);
                    break;

                case NamedPipeMessages.PostIndexChanged.NotificationRequest:
                    this.HandlePostIndexChangedRequest(message, connection);
                    break;

                case NamedPipeMessages.RunPostFetchJob.PostFetchJob:
                    this.HandlePostFetchJobRequest(message, connection);
                    break;

                case NamedPipeMessages.DehydrateFolders.Dehydrate:
                    this.HandleDehydrateFolders(message, connection);
                    break;

                default:
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "Mount");
                    metadata.Add("Header", message.Header);
                    this.tracer.RelatedError(metadata, "HandleRequest: Unknown request");

                    connection.TrySendResponse(NamedPipeMessages.UnknownRequest);
                    break;
            }
        }

        private void HandleDehydrateFolders(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.DehydrateFolders.Request request = new NamedPipeMessages.DehydrateFolders.Request(message);

            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(request.Folders), request.Folders);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, "Received dehydrate folders request");
            this.tracer.RelatedEvent(EventLevel.Informational, nameof(this.HandleDehydrateFolders), metadata);

            NamedPipeMessages.DehydrateFolders.Response response;
            if (this.currentState == MountState.Ready)
            {
                response = new NamedPipeMessages.DehydrateFolders.Response(NamedPipeMessages.DehydrateFolders.DehydratedResult);
                string[] folders = request.Folders.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder resetFolderPaths = new StringBuilder();
                foreach (string folder in folders)
                {
                    if (this.fileSystemCallbacks.TryDehydrateFolder(folder, out string errorMessage))
                    {
                        response.SuccessfulFolders.Add(folder);
                    }
                    else
                    {
                        response.FailedFolders.Add($"{folder}\0{errorMessage}");
                    }

                    resetFolderPaths.Append($"\"{folder.Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator)}\" ");
                }

                // Since modified paths could have changed with the dehydrate, the paths that were dehydrated need to be reset in the index
                string resetPaths = resetFolderPaths.ToString();
                GitProcess gitProcess = new GitProcess(this.enlistment);

                EventMetadata resetIndexMetadata = new EventMetadata();
                resetIndexMetadata.Add(nameof(resetPaths), resetPaths);

                GitProcess.Result refreshIndexResult;
                this.resetForDehydrateInProgress = true;
                try
                {
                    // Because we've set resetForDehydrateInProgress to true, this call to 'git reset' will also force
                    // the projection to be updated (required because 'git reset' will adjust the skip worktree bits in
                    // the index).
                    refreshIndexResult = gitProcess.Reset(GVFSConstants.DotGit.HeadName, resetPaths);
                }
                finally
                {
                    this.resetForDehydrateInProgress = false;
                }

                resetIndexMetadata.Add(nameof(refreshIndexResult.ExitCode), refreshIndexResult.ExitCode);
                resetIndexMetadata.Add(nameof(refreshIndexResult.Output), refreshIndexResult.Output);
                resetIndexMetadata.Add(nameof(refreshIndexResult.Errors), refreshIndexResult.Errors);
                resetIndexMetadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.HandleDehydrateFolders)}: Reset git index");
                this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.HandleDehydrateFolders)}_ResetIndex", resetIndexMetadata);
            }
            else
            {
                response = new NamedPipeMessages.DehydrateFolders.Response(NamedPipeMessages.DehydrateFolders.MountNotReadyResult);
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandleLockRequest(string messageBody, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.AcquireLock.Response response;

            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(messageBody);
            NamedPipeMessages.LockData requester = request.RequestData;
            if (this.currentState == MountState.Unmounting)
            {
                response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.UnmountInProgressResult);

                EventMetadata metadata = new EventMetadata();
                metadata.Add("LockRequest", requester.ToString());
                metadata.Add(TracingConstants.MessageKey.InfoMessage, "Request denied, unmount in progress");
                this.tracer.RelatedEvent(EventLevel.Informational, "HandleLockRequest_UnmountInProgress", metadata);
            }
            else if (this.currentState != MountState.Ready)
            {
                response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.MountNotReadyResult);
            }
            else
            {
                bool lockAcquired = false;

                NamedPipeMessages.LockData existingExternalHolder = null;
                string denyGVFSMessage = null;

                bool lockAvailable = this.context.Repository.GVFSLock.IsLockAvailableForExternalRequestor(out existingExternalHolder);
                bool isReadyForExternalLockRequests = this.fileSystemCallbacks.IsReadyForExternalAcquireLockRequests(requester, out denyGVFSMessage);

                if (!requester.CheckAvailabilityOnly && isReadyForExternalLockRequests)
                {
                    lockAcquired = this.context.Repository.GVFSLock.TryAcquireLockForExternalRequestor(requester, out existingExternalHolder);
                }

                if (requester.CheckAvailabilityOnly && lockAvailable && isReadyForExternalLockRequests)
                {
                    response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.AvailableResult);
                }
                else if (lockAcquired)
                {
                    response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.AcceptResult);
                    this.tracer.SetGitCommandSessionId(requester.GitCommandSessionId);
                }
                else if (existingExternalHolder == null)
                {
                    response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.DenyGVFSResult, responseData: null, denyGVFSMessage: denyGVFSMessage);
                }
                else
                {
                    response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.DenyGitResult, existingExternalHolder);
                }
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandleReleaseLockRequest(string messageBody, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(messageBody);

            if (request.RequestData == null)
            {
                this.tracer.RelatedError($"{nameof(this.HandleReleaseLockRequest)} received invalid lock request with body '{messageBody}'");
                this.UnmountAndStopWorkingDirectoryCallbacks();
                Environment.Exit((int)ReturnCode.NullRequestData);
            }

            NamedPipeMessages.ReleaseLock.Response response = this.fileSystemCallbacks.TryReleaseExternalLock(request.RequestData.PID);
            if (response.Result == NamedPipeMessages.ReleaseLock.SuccessResult)
            {
                this.tracer.SetGitCommandSessionId(string.Empty);
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandlePostIndexChangedRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.PostIndexChanged.Response response;
            NamedPipeMessages.PostIndexChanged.Request request = new NamedPipeMessages.PostIndexChanged.Request(message);
            if (request == null)
            {
                response = new NamedPipeMessages.PostIndexChanged.Response(NamedPipeMessages.UnknownRequest);
            }
            else if (this.currentState != MountState.Ready)
            {
                response = new NamedPipeMessages.PostIndexChanged.Response(NamedPipeMessages.MountNotReadyResult);
            }
            else
            {
                if (this.resetForDehydrateInProgress)
                {
                    // To avoid having to parse the index twice when dehydrating folders, repurpose the PostIndexChangedRequest
                    // for git reset to rebuild the projection.  Additionally, if we were to call ForceIndexProjectionUpdate
                    // directly in HandleDehydrateFolders we'd have a race condition where the OnIndexWriteRequiringModifiedPathsValidation
                    // background task would be trying to parse the index at the same time as HandleDehydrateFolders

                    this.fileSystemCallbacks.ForceIndexProjectionUpdate(invalidateProjection: true, invalidateModifiedPaths: false);
                }
                else
                {
                    this.fileSystemCallbacks.ForceIndexProjectionUpdate(request.UpdatedWorkingDirectory, request.UpdatedSkipWorktreeBits);
                }

                response = new NamedPipeMessages.PostIndexChanged.Response(NamedPipeMessages.PostIndexChanged.SuccessResult);
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandleModifiedPathsListRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.ModifiedPaths.Response response;
            NamedPipeMessages.ModifiedPaths.Request request = new NamedPipeMessages.ModifiedPaths.Request(message);
            if (request == null)
            {
                response = new NamedPipeMessages.ModifiedPaths.Response(NamedPipeMessages.UnknownRequest);
            }
            else if (this.currentState != MountState.Ready)
            {
                response = new NamedPipeMessages.ModifiedPaths.Response(NamedPipeMessages.MountNotReadyResult);
            }
            else
            {
                if (request.Version != NamedPipeMessages.ModifiedPaths.CurrentVersion)
                {
                    response = new NamedPipeMessages.ModifiedPaths.Response(NamedPipeMessages.ModifiedPaths.InvalidVersion);
                }
                else
                {
                    string data = string.Join("\0", this.fileSystemCallbacks.GetAllModifiedPaths()) + "\0";
                    response = new NamedPipeMessages.ModifiedPaths.Response(NamedPipeMessages.ModifiedPaths.SuccessResult, data);
                }
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandleDownloadObjectRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.DownloadObject.Response response;

            NamedPipeMessages.DownloadObject.Request request = new NamedPipeMessages.DownloadObject.Request(message);
            string objectSha = request.RequestSha;
            if (this.currentState != MountState.Ready)
            {
                response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.MountNotReadyResult);
            }
            else
            {
                if (!SHA1Util.IsValidShaFormat(objectSha))
                {
                    response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.DownloadObject.InvalidSHAResult);
                }
                else
                {
                    Stopwatch downloadTime = Stopwatch.StartNew();

                    /* If this is the root tree for a commit that was was just downloaded, assume that more
                     * trees will be needed soon and download them as well by using the download commit API.
                     *
                     * Otherwise, or as a fallback if the commit download fails, download the object directly.
                     */
                    if (this.ShouldDownloadCommitPack(objectSha, out string commitSha)
                        && this.gitObjects.TryDownloadCommit(commitSha))
                    {
                        this.DownloadedCommitPack(objectSha: objectSha, commitSha: commitSha);
                        response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.DownloadObject.SuccessResult);
                        // FUTURE: Should the stats be updated to reflect all the trees in the pack?
                        // FUTURE: Should we try to clean up duplicate trees or increase depth of the commit download?
                    }
                    else if (this.gitObjects.TryDownloadAndSaveObject(objectSha, GVFSGitObjects.RequestSource.NamedPipeMessage) == GitObjects.DownloadAndSaveObjectResult.Success)
                    {
                        response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.DownloadObject.SuccessResult);
                    }
                    else
                    {
                        response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.DownloadObject.DownloadFailed);
                    }


                    Native.ObjectTypes? objectType;
                    this.context.Repository.TryGetObjectType(objectSha, out objectType);
                    this.context.Repository.GVFSLock.Stats.RecordObjectDownload(objectType == Native.ObjectTypes.Blob, downloadTime.ElapsedMilliseconds);

                    if (objectType == Native.ObjectTypes.Commit
                        && !this.PrefetchHasBeenDone()
                        && !this.context.Repository.CommitAndRootTreeExists(objectSha, out var treeSha)
                        && !string.IsNullOrEmpty(treeSha))
                    {
                        /* If a commit is downloaded, it wasn't prefetched.
                         * If any prefetch has been done, there is probably a commit in the prefetch packs that is close enough that
                         * loose object download of missing trees will be faster than downloading a pack of all the trees for the commit.
                         * Otherwise, the trees for the commit may be needed soon depending on the context. 
                         * e.g. git log (without a pathspec) doesn't need trees, but git checkout does.
                         * 
                         * Save the tree/commit so if the tree is requested soon we can download all the trees for the commit in a batch.
                         */
                        this.treesWithDownloadedCommits[treeSha] = objectSha;
                    }
                }
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private bool PrefetchHasBeenDone()
        {
            var prefetchPacks = this.gitObjects.ReadPackFileNames(this.enlistment.GitPackRoot, GVFSConstants.PrefetchPackPrefix);
            return prefetchPacks.Length > 0;
        }

        private bool ShouldDownloadCommitPack(string objectSha, out string commitSha)
        {

            if (!this.treesWithDownloadedCommits.TryGetValue(objectSha, out commitSha)
                || this.PrefetchHasBeenDone())
            {
                return false;
            }

            /* This is a heuristic to prevent downloading multiple packs related to git history commands,
             * since commits downloaded close together likely have similar trees. */
            var timePassed = DateTime.UtcNow - this.lastCommitPackDownloadTime;
            return (timePassed > TimeSpan.FromMinutes(5));
        }

        private void DownloadedCommitPack(string objectSha, string commitSha)
        {
            this.lastCommitPackDownloadTime = DateTime.UtcNow;
            this.treesWithDownloadedCommits.Remove(objectSha);
        }

        private void HandlePostFetchJobRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(message);

            this.tracer.RelatedInfo("Received post-fetch job request with body {0}", message.Body);

            NamedPipeMessages.RunPostFetchJob.Response response;
            if (this.currentState == MountState.Ready)
            {
                List<string> packIndexes = JsonConvert.DeserializeObject<List<string>>(message.Body);
                this.maintenanceScheduler.EnqueueOneTimeStep(new PostFetchStep(this.context, packIndexes));

                response = new NamedPipeMessages.RunPostFetchJob.Response(NamedPipeMessages.RunPostFetchJob.QueuedResult);
            }
            else
            {
                response = new NamedPipeMessages.RunPostFetchJob.Response(NamedPipeMessages.RunPostFetchJob.MountNotReadyResult);
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private void HandleGetStatusRequest(NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.GetStatus.Response response = new NamedPipeMessages.GetStatus.Response();
            response.EnlistmentRoot = this.enlistment.EnlistmentRoot;
            response.LocalCacheRoot = !string.IsNullOrWhiteSpace(this.enlistment.LocalCacheRoot) ? this.enlistment.LocalCacheRoot : this.enlistment.GitObjectsRoot;
            response.RepoUrl = this.enlistment.RepoUrl;
            response.CacheServer = this.cacheServer.ToString();
            response.LockStatus = this.context?.Repository.GVFSLock != null ? this.context.Repository.GVFSLock.GetStatus() : "Unavailable";
            response.DiskLayoutVersion = $"{GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMajorVersion}.{GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMinorVersion}";

            switch (this.currentState)
            {
                case MountState.Mounting:
                    response.MountStatus = NamedPipeMessages.GetStatus.Mounting;
                    break;

                case MountState.Ready:
                    response.MountStatus = NamedPipeMessages.GetStatus.Ready;
                    response.BackgroundOperationCount = this.fileSystemCallbacks.BackgroundOperationCount;
                    break;

                case MountState.Unmounting:
                    response.MountStatus = NamedPipeMessages.GetStatus.Unmounting;
                    break;

                case MountState.MountFailed:
                    response.MountStatus = NamedPipeMessages.GetStatus.MountFailed;
                    break;

                default:
                    response.MountStatus = NamedPipeMessages.UnknownGVFSState;
                    break;
            }

            connection.TrySendResponse(response.ToJson());
        }

        private void HandleUnmountRequest(NamedPipeServer.Connection connection)
        {
            switch (this.currentState)
            {
                case MountState.Mounting:
                    connection.TrySendResponse(NamedPipeMessages.Unmount.NotMounted);
                    break;

                // Even if the previous mount failed, attempt to unmount anyway.  Otherwise the user has no
                // recourse but to kill the process.
                case MountState.MountFailed:
                    goto case MountState.Ready;

                case MountState.Ready:
                    this.currentState = MountState.Unmounting;

                    connection.TrySendResponse(NamedPipeMessages.Unmount.Acknowledged);
                    this.UnmountAndStopWorkingDirectoryCallbacks();
                    connection.TrySendResponse(NamedPipeMessages.Unmount.Completed);

                    this.unmountEvent.Set();
                    Environment.Exit((int)ReturnCode.Success);
                    break;

                case MountState.Unmounting:
                    connection.TrySendResponse(NamedPipeMessages.Unmount.AlreadyUnmounting);
                    break;

                default:
                    connection.TrySendResponse(NamedPipeMessages.UnknownGVFSState);
                    break;
            }
        }

        private void MountAndStartWorkingDirectoryCallbacks(CacheServerInfo cache)
        {
            string error;
            if (!this.context.Enlistment.Authentication.TryInitialize(this.context.Tracer, this.context.Enlistment, out error))
            {
                this.FailMountAndExit("Failed to obtain git credentials: " + error);
            }

            GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(this.context.Tracer, this.context.Enlistment, cache, this.retryConfig);
            this.gitObjects = new GVFSGitObjects(this.context, objectRequestor);
            FileSystemVirtualizer virtualizer = this.CreateOrReportAndExit(() => GVFSPlatformLoader.CreateFileSystemVirtualizer(this.context, this.gitObjects), "Failed to create src folder virtualizer");

            GitStatusCache gitStatusCache = (!this.context.Unattended && GVFSPlatform.Instance.IsGitStatusCacheSupported()) ? new GitStatusCache(this.context, this.gitStatusCacheConfig) : null;
            if (gitStatusCache != null)
            {
                this.tracer.RelatedInfo("Git status cache enabled. Backoff time: {0}ms", this.gitStatusCacheConfig.BackoffTime.TotalMilliseconds);
            }
            else
            {
                this.tracer.RelatedInfo("Git status cache is not enabled");
            }

            this.gvfsDatabase = this.CreateOrReportAndExit(() => new GVFSDatabase(this.context.FileSystem, this.context.Enlistment.EnlistmentRoot, new SqliteDatabase()), "Failed to create database connection");
            this.fileSystemCallbacks = this.CreateOrReportAndExit(
                () =>
                {
                    return new FileSystemCallbacks(
                        this.context,
                        this.gitObjects,
                        RepoMetadata.Instance,
                        blobSizes: null,
                        gitIndexProjection: null,
                        backgroundFileSystemTaskRunner: null,
                        fileSystemVirtualizer: virtualizer,
                        placeholderDatabase: new PlaceholderTable(this.gvfsDatabase),
                        sparseCollection: new SparseTable(this.gvfsDatabase),
                        gitStatusCache: gitStatusCache);
                }, "Failed to create src folder callback listener");
            this.maintenanceScheduler = this.CreateOrReportAndExit(() => new GitMaintenanceScheduler(this.context, this.gitObjects), "Failed to start maintenance scheduler");

            int majorVersion;
            int minorVersion;
            if (!RepoMetadata.Instance.TryGetOnDiskLayoutVersion(out majorVersion, out minorVersion, out error))
            {
                this.FailMountAndExit("Error: {0}", error);
            }

            if (majorVersion != GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMajorVersion)
            {
                this.FailMountAndExit(
                    "Error: On disk version ({0}) does not match current version ({1})",
                    majorVersion,
                    GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMajorVersion);
            }

            try
            {
                if (!this.fileSystemCallbacks.TryStart(out error))
                {
                    this.FailMountAndExit("Error: {0}. \r\nPlease confirm that gvfs clone completed without error.", error);
                }
            }
            catch (Exception e)
            {
                this.FailMountAndExit("Failed to initialize src folder callbacks. {0}", e.ToString());
            }

            this.heartbeat = new HeartbeatThread(this.tracer, this.fileSystemCallbacks);
            this.heartbeat.Start();
        }

        private void UnmountAndStopWorkingDirectoryCallbacks()
        {
            if (this.maintenanceScheduler != null)
            {
                this.maintenanceScheduler.Dispose();
                this.maintenanceScheduler = null;
            }

            if (this.heartbeat != null)
            {
                this.heartbeat.Stop();
                this.heartbeat = null;
            }

            if (this.fileSystemCallbacks != null)
            {
                this.fileSystemCallbacks.Stop();
                this.fileSystemCallbacks.Dispose();
                this.fileSystemCallbacks = null;
            }

            this.gvfsDatabase?.Dispose();
            this.gvfsDatabase = null;
        }
    }
}