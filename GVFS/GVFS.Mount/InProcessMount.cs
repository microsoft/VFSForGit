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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static GVFS.Common.Git.LibGit2Repo;

namespace GVFS.Mount
{
    public class InProcessMount
    {
        // Tests show that 250 is the max supported pipe name length
        private const int MaxPipeNameLength = 250;
        private const int MutexMaxWaitTimeMS = 500;

        // This is value chosen based on tested scenarios to limit the required download time for
        // all the trees. This is approximately the amount of trees that can be downloaded in 1 second.
        // Downloading an entire commit pack also takes around 1 second, so this should limit downloading
        // all the trees in a commit to ~2-3 seconds.
        private const int MissingTreeThresholdForDownloadingCommitPack = 200;

        // Number of unique missing trees to track with LRU eviction. Eviction is commit-based:
        // when capacity is reached, the LRU commit and all its unique trees are dropped to make room.
        // Set to 20x the threshold so that enough trees can accumulate for the heuristic to
        // reliably trigger a commit pack download.
        private const int TrackedTreeCapacity = MissingTreeThresholdForDownloadingCommitPack * 20;

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

        private readonly MissingTreeTracker missingTreeTracker;

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
            this.missingTreeTracker = new MissingTreeTracker(tracer, TrackedTreeCapacity);
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

            // For worktree mounts, create the .gvfs metadata directory and
            // bootstrap it with cache paths from the primary enlistment
            if (this.enlistment.IsWorktree)
            {
                this.InitializeWorktreeMetadata();
            }

            string mountLockPath = Path.Combine(this.enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.MountLock);
            using (FileBasedLock mountLock = GVFSPlatform.Instance.CreateFileBasedLock(
                new PhysicalFileSystem(),
                this.tracer,
                mountLockPath))
            {
                if (!mountLock.TryAcquireLock(out Exception lockException))
                {
                    if (lockException is IOException)
                    {
                        this.FailMountAndExit(ReturnCode.MountAlreadyRunning, "Mount: Another mount process is already running.");
                    }

                    this.FailMountAndExit("Mount: Failed to acquire mount lock: {0}", lockException.Message);
                }

                this.MountWithLockAcquired(verbosity, keywords);
            }
        }

        private void MountWithLockAcquired(EventLevel verbosity, Keywords keywords)
        {
            // Start auth + config query immediately — these are network-bound and don't
            // depend on repo metadata or cache paths. Every millisecond of network latency
            // we can overlap with local I/O is a win.
            // TryInitializeAndQueryGVFSConfig combines the anonymous probe, credential fetch,
            // and config query into at most 2 HTTP requests (1 for anonymous repos), reusing
            // the same HttpClient/TCP connection.
            Stopwatch parallelTimer = Stopwatch.StartNew();

            var networkTask = Task.Run(() =>
            {
                Stopwatch sw = Stopwatch.StartNew();
                ServerGVFSConfig config;
                string authConfigError;

                if (!this.enlistment.Authentication.TryInitializeAndQueryGVFSConfig(
                    this.tracer, this.enlistment, this.retryConfig,
                    out config, out authConfigError))
                {
                    if (this.cacheServer != null && !string.IsNullOrWhiteSpace(this.cacheServer.Url))
                    {
                        this.tracer.RelatedWarning("Mount will proceed with fallback cache server: " + authConfigError);
                        config = null;
                    }
                    else
                    {
                        this.FailMountAndExit("Unable to query /gvfs/config" + Environment.NewLine + authConfigError);
                    }
                }

                this.ValidateGVFSVersion(config);
                this.tracer.RelatedInfo("ParallelMount: Auth + config completed in {0}ms", sw.ElapsedMilliseconds);
                return config;
            });
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

            // Local validations and git config run while we wait for the network
            var localTask = Task.Run(() =>
            {
                Stopwatch sw = Stopwatch.StartNew();

                this.ValidateGitVersion();
                this.tracer.RelatedInfo("ParallelMount: ValidateGitVersion completed in {0}ms", sw.ElapsedMilliseconds);

                this.ValidateHooksVersion();
                this.ValidateFileSystemSupportsRequiredFeatures();

                GitProcess git = new GitProcess(this.enlistment);
                if (!git.IsValidRepo())
                {
                    this.FailMountAndExit("The .git folder is missing or has invalid contents");
                }

                if (!GVFSPlatform.Instance.FileSystem.IsFileSystemSupported(this.enlistment.EnlistmentRoot, out string fsError))
                {
                    this.FailMountAndExit("FileSystem unsupported: " + fsError);
                }

                this.tracer.RelatedInfo("ParallelMount: Local validations completed in {0}ms", sw.ElapsedMilliseconds);

                if (!this.TrySetRequiredGitConfigSettings())
                {
                    this.FailMountAndExit("Unable to configure git repo");
                }

                this.LogEnlistmentInfoAndSetConfigValues();
                this.tracer.RelatedInfo("ParallelMount: Local validations + git config completed in {0}ms", sw.ElapsedMilliseconds);
            });

            try
            {
                Task.WaitAll(networkTask, localTask);
            }
            catch (AggregateException ae)
            {
                this.FailMountAndExit(ae.Flatten().InnerExceptions[0].Message);
            }

            parallelTimer.Stop();
            this.tracer.RelatedInfo("ParallelMount: All parallel tasks completed in {0}ms", parallelTimer.ElapsedMilliseconds);

            ServerGVFSConfig serverGVFSConfig = networkTask.Result;

            CacheServerResolver cacheServerResolver = new CacheServerResolver(this.tracer, this.enlistment);
            this.cacheServer = cacheServerResolver.ResolveNameFromRemote(this.cacheServer.Url, serverGVFSConfig);

            this.EnsureLocalCacheIsHealthy(serverGVFSConfig);

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

                // Worktrees share hooks with the primary enlistment via core.hookspath,
                // so skip installation to avoid locking conflicts with the running mount.
                if (!this.enlistment.IsWorktree && !HooksInstaller.TryUpdateHooks(this.context, out errorMessage))
                {
                    this.FailMountAndExit(errorMessage);
                }

                GVFSPlatform.Instance.ConfigureVisualStudio(this.enlistment.GitBinPath, this.tracer);

                this.MountAndStartWorkingDirectoryCallbacks(this.cacheServer);

                try
                {
                    Console.Title = "GVFS " + ProcessHelper.GetCurrentProcessVersion() + " - " + this.enlistment.EnlistmentRoot;
                }
                catch (IOException)
                {
                    // Console.Title throws when the process has no console (e.g. started as background/hidden process)
                }

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

            if (this.enlistment.IsWorktree)
            {
                // Worktrees have a .git file (not directory) pointing to the shared git dir
                string dotGitFile = Path.Combine(this.enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Root);
                if (!File.Exists(dotGitFile))
                {
                    this.FailMountAndExit("Failed to mount worktree. File \"{0}\" must exist.", dotGitFile);
                }
            }
            else
            {
                string dotGitPath = Path.Combine(this.enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Root);
                DirectoryInfo dotGitPathInfo = new DirectoryInfo(dotGitPath);
                if (!dotGitPathInfo.Exists)
                {
                    this.FailMountAndExit("Failed to mount. Directory \"{0}\" must exist.", dotGitPathInfo);
                }
            }
        }

        /// <summary>
        /// For worktree mounts, create the .gvfs metadata directory and
        /// bootstrap RepoMetadata with cache paths from the primary enlistment.
        /// </summary>
        private void InitializeWorktreeMetadata()
        {
            string dotGVFSRoot = this.enlistment.DotGVFSRoot;
            if (!Directory.Exists(dotGVFSRoot))
            {
                try
                {
                    Directory.CreateDirectory(dotGVFSRoot);
                    this.tracer.RelatedInfo($"Created worktree metadata directory: {dotGVFSRoot}");
                }
                catch (Exception e)
                {
                    this.FailMountAndExit("Failed to create worktree metadata directory '{0}': {1}", dotGVFSRoot, e.Message);
                }
            }

            // Bootstrap RepoMetadata from the primary enlistment's metadata.
            // Use try/finally to guarantee Shutdown() even if an unexpected
            // exception occurs — the singleton must not be left pointing at
            // the primary's metadata directory.
            string primaryDotGVFS = Path.Combine(this.enlistment.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            string error;
            string gitObjectsRoot;
            string localCacheRoot;
            string blobSizesRoot;

            if (!RepoMetadata.TryInitialize(this.tracer, primaryDotGVFS, out error))
            {
                this.FailMountAndExit("Failed to read primary enlistment metadata: " + error);
            }

            try
            {
                if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
                {
                    this.FailMountAndExit("Failed to read git objects root from primary metadata: " + error);
                }

                if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
                {
                    this.FailMountAndExit("Failed to read local cache root from primary metadata: " + error);
                }

                if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out blobSizesRoot, out error))
                {
                    this.FailMountAndExit("Failed to read blob sizes root from primary metadata: " + error);
                }
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            // Initialize cache paths on the enlistment so SaveCloneMetadata
            // can persist them into the worktree's metadata
            this.enlistment.InitializeCachePaths(localCacheRoot, gitObjectsRoot, blobSizesRoot);

            // Initialize the worktree's own metadata with cache paths,
            // disk layout version, and a new enlistment ID
            if (!RepoMetadata.TryInitialize(this.tracer, dotGVFSRoot, out error))
            {
                this.FailMountAndExit("Failed to initialize worktree metadata: " + error);
            }

            try
            {
                RepoMetadata.Instance.SaveCloneMetadata(this.tracer, this.enlistment);
            }
            finally
            {
                RepoMetadata.Shutdown();
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
            this.FailMountAndExit(ReturnCode.GenericError, error, args);
        }

        private void FailMountAndExit(ReturnCode returnCode, string error, params object[] args)
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

            Environment.Exit((int)returnCode);
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

                case NamedPipeMessages.PrepareForUnstage.Request:
                    this.HandlePrepareForUnstageRequest(message, connection);
                    break;

                case NamedPipeMessages.RunPostFetchJob.PostFetchJob:
                    this.HandlePostFetchJobRequest(message, connection);
                    break;

                case NamedPipeMessages.DehydrateFolders.Dehydrate:
                    this.HandleDehydrateFolders(message, connection);
                    break;

                case NamedPipeMessages.HydrationStatus.Request:
                    this.HandleGetHydrationStatusRequest(connection);
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

        private void HandleGetHydrationStatusRequest(NamedPipeServer.Connection connection)
        {
            EnlistmentHydrationSummary summary = this.fileSystemCallbacks?.GetCachedHydrationSummary();
            if (summary == null || !summary.IsValid)
            {
                this.tracer.RelatedInfo(
                    $"{nameof(this.HandleGetHydrationStatusRequest)}: " +
                    (summary == null ? "No cached hydration summary available yet" : "Cached hydration summary is invalid"));

                connection.TrySendResponse(
                    new NamedPipeMessages.Message(NamedPipeMessages.HydrationStatus.NotAvailableResult, null));
                return;
            }

            NamedPipeMessages.HydrationStatus.Response response = new NamedPipeMessages.HydrationStatus.Response
            {
                PlaceholderFileCount = summary.PlaceholderFileCount,
                PlaceholderFolderCount = summary.PlaceholderFolderCount,
                ModifiedFileCount = summary.ModifiedFileCount,
                ModifiedFolderCount = summary.ModifiedFolderCount,
                TotalFileCount = summary.TotalFileCount,
                TotalFolderCount = summary.TotalFolderCount,
            };

            connection.TrySendResponse(
                new NamedPipeMessages.Message(NamedPipeMessages.HydrationStatus.SuccessResult, response.ToBody()));
        }

        private void HandleDehydrateFolders(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.DehydrateFolders.Request request = NamedPipeMessages.DehydrateFolders.Request.FromMessage(message);

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
                List<string> movedFolders = BackupFoldersWhileUnmounted(request, response, folders);

                foreach (string folder in movedFolders)
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

        private List<string> BackupFoldersWhileUnmounted(NamedPipeMessages.DehydrateFolders.Request request, NamedPipeMessages.DehydrateFolders.Response response, string[] folders)
        {
            /* We can't move folders while the virtual file system is mounted, so unmount it first.
             * After moving the folders, remount the virtual file system.
             */

            var movedFolders = new List<string>();
            try
            {
                /* Set to "Mounting" instead of "Unmounting" so that incoming requests
                 * that are rejected will know they can try again soon.
                 */
                this.currentState = MountState.Mounting;
                this.UnmountAndStopWorkingDirectoryCallbacks(willRemountInSameProcess: true);
                foreach (string folder in folders)
                {
                    try
                    {
                        var source = Path.Combine(this.enlistment.WorkingDirectoryBackingRoot, folder);
                        var destination = Path.Combine(request.BackupFolderPath, folder);
                        var destinationParent = Path.GetDirectoryName(destination);
                        this.context.FileSystem.CreateDirectory(destinationParent);
                        if (this.context.FileSystem.DirectoryExists(source))
                        {
                            this.context.FileSystem.MoveDirectory(source, destination);
                        }
                        movedFolders.Add(folder);
                    }
                    catch (Exception ex)
                    {
                        response.FailedFolders.Add($"{folder}\0{ex.Message}");
                        continue;
                    }
                }
            }
            finally
            {
                this.MountAndStartWorkingDirectoryCallbacks(this.cacheServer, alreadyInitialized: true);
                this.currentState = MountState.Ready;
            }

            return movedFolders;
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

        /// <summary>
        /// Handles a request to prepare for an unstage operation (e.g., restore --staged).
        /// Finds index entries that are staged (not in HEAD) with skip-worktree set and adds
        /// them to ModifiedPaths so that git will clear skip-worktree and process them.
        /// Also forces a projection update to fix stale placeholders for modified/deleted files.
        /// </summary>
        private void HandlePrepareForUnstageRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.PrepareForUnstage.Response response;

            if (this.currentState != MountState.Ready)
            {
                response = new NamedPipeMessages.PrepareForUnstage.Response(NamedPipeMessages.MountNotReadyResult);
            }
            else
            {
                try
                {
                    string pathspec = message.Body;
                    bool success = this.fileSystemCallbacks.AddStagedFilesToModifiedPaths(pathspec, out int addedCount);

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("addedToModifiedPaths", addedCount);
                    metadata.Add("pathspec", pathspec ?? "(all)");
                    metadata.Add("success", success);
                    this.tracer.RelatedEvent(
                        EventLevel.Informational,
                        nameof(this.HandlePrepareForUnstageRequest),
                        metadata);

                    response = new NamedPipeMessages.PrepareForUnstage.Response(
                        success
                            ? NamedPipeMessages.PrepareForUnstage.SuccessResult
                            : NamedPipeMessages.PrepareForUnstage.FailureResult);
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Exception", e.ToString());
                    this.tracer.RelatedError(metadata, nameof(this.HandlePrepareForUnstageRequest) + " failed");
                    response = new NamedPipeMessages.PrepareForUnstage.Response(NamedPipeMessages.PrepareForUnstage.FailureResult);
                }
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
                        this.DownloadedCommitPack(commitSha);
                        response = new NamedPipeMessages.DownloadObject.Response(NamedPipeMessages.DownloadObject.SuccessResult);
                        // FUTURE: Should the stats be updated to reflect all the trees in the pack?
                        // FUTURE: Should we try to clean up duplicate trees or increase depth of the commit download?
                    }
                    else if (this.gitObjects.TryDownloadAndSaveObject(objectSha, GVFSGitObjects.RequestSource.NamedPipeMessage) == GitObjects.DownloadAndSaveObjectResult.Success)
                    {
                        this.UpdateTreesForDownloadedCommits(objectSha);
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
                        && !this.context.Repository.CommitAndRootTreeExists(objectSha, out var treeSha)
                        && !string.IsNullOrEmpty(treeSha))
                    {
                        /* If a commit is downloaded, it wasn't prefetched.
                         * The trees for the commit may be needed soon depending on the context.
                         * e.g. git log (without a pathspec) doesn't need trees, but git checkout does.
                         *
                         * If any prefetch has been done there is probably a similar commit/tree in the graph,
                         * but in case there isn't (such as if the cache server repack maintenance job is failing)
                         * we should still try to avoid downloading an excessive number of loose trees for a commit.
                         *
                         * Save the tree/commit so if more trees are requested we can download all the trees for the commit in a batch.
                         */
                        this.missingTreeTracker.AddMissingRootTree(treeSha: treeSha, commitSha: objectSha);
                    }
                }
            }

            connection.TrySendResponse(response.CreateMessage());
        }

        private bool ShouldDownloadCommitPack(string objectSha, out string commitSha)
        {
            if (!this.missingTreeTracker.TryGetCommits(objectSha, out string[] commitShas))
            {
                commitSha = null;
                return false;
            }

            /* This is a heuristic to prevent downloading multiple packs related to git history commands.
             * Closely related commits are likely to have similar trees, so we'll find fewer missing trees in them.
             * Conversely, if we know (from previously downloaded missing trees) that a commit has a lot of missing
             * trees left, we'll probably need to download many more trees for the commit so we should download the pack.
             */
            int missingTreeCount = this.missingTreeTracker.GetHighestMissingTreeCount(commitShas, out commitSha);

            return missingTreeCount > MissingTreeThresholdForDownloadingCommitPack;
        }

        private void UpdateTreesForDownloadedCommits(string objectSha)
        {
            /* If we are downloading missing trees, we probably are missing more trees for the commit.
             * Update our list of trees associated with the commit so we can use the # of missing trees
             * as a heuristic to decide whether to batch download all the trees for the commit the
             * next time a missing one is requested.
             */
            if (!this.missingTreeTracker.TryGetCommits(objectSha, out _))
            {
                return;
            }

            if (!this.context.Repository.TryGetObjectType(objectSha, out var objectType)
                || objectType != Native.ObjectTypes.Tree)
            {
                return;
            }

            if (this.context.Repository.TryGetMissingSubTrees(objectSha, out var missingSubTrees))
            {
                this.missingTreeTracker.AddMissingSubTrees(objectSha, missingSubTrees);
            }
        }

        private void DownloadedCommitPack(string commitSha)
        {
            this.missingTreeTracker.MarkCommitComplete(commitSha);
        }

        private void HandlePostFetchJobRequest(NamedPipeMessages.Message message, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(message);

            this.tracer.RelatedInfo("Received post-fetch job request with body {0}", message.Body);

            NamedPipeMessages.RunPostFetchJob.Response response;
            if (this.currentState == MountState.Ready)
            {
                List<string> packIndexes = GVFSJsonOptions.Deserialize<List<string>>(message.Body);
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

        private void MountAndStartWorkingDirectoryCallbacks(CacheServerInfo cache, bool alreadyInitialized = false)
        {
            string error;

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

            if (!alreadyInitialized)
            {
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

        private void ValidateGitVersion()
        {
            GitVersion gitVersion = null;
            if (string.IsNullOrEmpty(this.enlistment.GitBinPath) || !GitProcess.TryGetVersion(this.enlistment.GitBinPath, out gitVersion, out string _))
            {
                this.FailMountAndExit("Error: Unable to retrieve the Git version");
            }

            this.enlistment.SetGitVersion(gitVersion.ToString());

            if (gitVersion.Platform != GVFSConstants.SupportedGitVersion.Platform)
            {
                this.FailMountAndExit("Error: Invalid version of Git {0}. Must use vfs version.", gitVersion);
            }

            if (gitVersion.IsLessThan(GVFSConstants.SupportedGitVersion))
            {
                this.FailMountAndExit(
                    "Error: Installed Git version {0} is less than the minimum supported version of {1}.",
                    gitVersion,
                    GVFSConstants.SupportedGitVersion);
            }
            else if (gitVersion.Revision != GVFSConstants.SupportedGitVersion.Revision)
            {
                this.FailMountAndExit(
                    "Error: Installed Git version {0} has revision number {1} instead of {2}."
                    + " This Git version is too new, so either downgrade Git or upgrade VFS for Git."
                    + " The minimum supported version of Git is {3}.",
                    gitVersion,
                    gitVersion.Revision,
                    GVFSConstants.SupportedGitVersion.Revision,
                    GVFSConstants.SupportedGitVersion);
            }
        }

        private void ValidateHooksVersion()
        {
            string hooksVersion;
            string error;
            if (!GVFSPlatform.Instance.TryGetGVFSHooksVersion(out hooksVersion, out error))
            {
                this.FailMountAndExit(error);
            }

            string gvfsVersion = ProcessHelper.GetCurrentProcessVersion();
            if (hooksVersion != gvfsVersion)
            {
                this.FailMountAndExit("GVFS.Hooks version ({0}) does not match GVFS version ({1}).", hooksVersion, gvfsVersion);
            }

            this.enlistment.SetGVFSHooksVersion(hooksVersion);
        }

        private void ValidateFileSystemSupportsRequiredFeatures()
        {
            try
            {
                string warning;
                string error;
                if (!GVFSPlatform.Instance.KernelDriver.IsSupported(this.enlistment.EnlistmentRoot, out warning, out error))
                {
                    this.FailMountAndExit("Error: {0}", error);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                this.tracer.RelatedError(metadata, "Failed to determine if file system supports features required by GVFS");
                this.FailMountAndExit("Error: Failed to determine if file system supports features required by GVFS.");
            }
        }

        private ServerGVFSConfig QueryAndValidateGVFSConfig()
        {
            ServerGVFSConfig serverGVFSConfig = null;
            string errorMessage = null;

            using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(this.tracer, this.enlistment, this.retryConfig))
            {
                const bool LogErrors = true;
                if (!configRequestor.TryQueryGVFSConfig(LogErrors, out serverGVFSConfig, out _, out errorMessage))
                {
                    // If we have a valid cache server, continue without config (matches verb fallback behavior)
                    if (this.cacheServer != null && !string.IsNullOrWhiteSpace(this.cacheServer.Url))
                    {
                        this.tracer.RelatedWarning("Unable to query /gvfs/config: " + errorMessage);
                        serverGVFSConfig = null;
                    }
                    else
                    {
                        this.FailMountAndExit("Unable to query /gvfs/config" + Environment.NewLine + errorMessage);
                    }
                }
            }

            this.ValidateGVFSVersion(serverGVFSConfig);

            return serverGVFSConfig;
        }

        private void ValidateGVFSVersion(ServerGVFSConfig config)
        {
            using (ITracer activity = this.tracer.StartActivity("ValidateGVFSVersion", EventLevel.Informational))
            {
                if (ProcessHelper.IsDevelopmentVersion())
                {
                    return;
                }

                string recordedVersion = ProcessHelper.GetCurrentProcessVersion();
                int plus = recordedVersion.IndexOf('+');
                Version currentVersion = new Version(plus < 0 ? recordedVersion : recordedVersion.Substring(0, plus));
                IEnumerable<ServerGVFSConfig.VersionRange> allowedGvfsClientVersions =
                    config != null
                    ? config.AllowedGVFSClientVersions
                    : null;

                if (allowedGvfsClientVersions == null || !allowedGvfsClientVersions.Any())
                {
                    string warningMessage = "WARNING: Unable to validate your GVFS version" + Environment.NewLine;
                    if (config == null)
                    {
                        warningMessage += "Could not query valid GVFS versions from: " + Uri.EscapeDataString(this.enlistment.RepoUrl);
                    }
                    else
                    {
                        warningMessage += "Server not configured to provide supported GVFS versions";
                    }

                    this.tracer.RelatedWarning(warningMessage);
                    return;
                }

                foreach (ServerGVFSConfig.VersionRange versionRange in config.AllowedGVFSClientVersions)
                {
                    if (currentVersion >= versionRange.Min &&
                        (versionRange.Max == null || currentVersion <= versionRange.Max))
                    {
                        activity.RelatedEvent(
                            EventLevel.Informational,
                            "GVFSVersionValidated",
                            new EventMetadata
                            {
                                { "SupportedVersionRange", versionRange },
                            });

                        this.enlistment.SetGVFSVersion(currentVersion.ToString());
                        return;
                    }
                }

                activity.RelatedError("GVFS version {0} is not supported", currentVersion);
                this.FailMountAndExit("ERROR: Your GVFS version is no longer supported. Install the latest and try again.");
            }
        }

        private void EnsureLocalCacheIsHealthy(ServerGVFSConfig serverGVFSConfig)
        {
            if (!Directory.Exists(this.enlistment.LocalCacheRoot))
            {
                try
                {
                    this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Local cache root: {this.enlistment.LocalCacheRoot} missing, recreating it");
                    Directory.CreateDirectory(this.enlistment.LocalCacheRoot);
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("enlistment.LocalCacheRoot", this.enlistment.LocalCacheRoot);
                    this.tracer.RelatedError(metadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to create local cache root");
                    this.FailMountAndExit("Failed to create local cache: " + this.enlistment.LocalCacheRoot);
                }
            }

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            if (Directory.Exists(this.enlistment.GitObjectsRoot))
            {
                bool gitObjectsRootInAlternates = false;
                string alternatesFilePath = Path.Combine(this.enlistment.DotGitRoot, GVFSConstants.DotGit.Objects.Info.AlternatesRelativePath);
                if (File.Exists(alternatesFilePath))
                {
                    try
                    {
                        using (Stream stream = fileSystem.OpenFileStream(
                            alternatesFilePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            callFlushFileBuffers: false))
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                while (!reader.EndOfStream)
                                {
                                    string alternatesLine = reader.ReadLine();
                                    if (string.Equals(alternatesLine, this.enlistment.GitObjectsRoot, GVFSPlatform.Instance.Constants.PathComparison))
                                    {
                                        gitObjectsRootInAlternates = true;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        EventMetadata exceptionMetadata = new EventMetadata();
                        exceptionMetadata.Add("Exception", e.ToString());
                        this.tracer.RelatedError(exceptionMetadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to validate alternates file");
                        this.FailMountAndExit($"Failed to validate that alternates file includes git objects root: {e.Message}");
                    }
                }
                else
                {
                    this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Alternates file not found");
                }

                if (!gitObjectsRootInAlternates)
                {
                    this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: GitObjectsRoot ({this.enlistment.GitObjectsRoot}) missing from alternates files, recreating alternates");
                    string error;
                    if (!this.TryCreateAlternatesFile(fileSystem, out error))
                    {
                        this.FailMountAndExit($"Failed to update alternates file to include git objects root: {error}");
                    }
                }
            }
            else
            {
                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: GitObjectsRoot ({this.enlistment.GitObjectsRoot}) missing, determining new root");

                if (serverGVFSConfig == null)
                {
                    using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(this.tracer, this.enlistment, this.retryConfig))
                    {
                        string configError;
                        if (!configRequestor.TryQueryGVFSConfig(true, out serverGVFSConfig, out _, out configError))
                        {
                            this.FailMountAndExit("Unable to query /gvfs/config" + Environment.NewLine + configError);
                        }
                    }
                }

                string localCacheKey;
                string error;
                LocalCacheResolver localCacheResolver = new LocalCacheResolver(this.enlistment);
                if (!localCacheResolver.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers(
                    this.tracer,
                    serverGVFSConfig,
                    this.cacheServer,
                    this.enlistment.LocalCacheRoot,
                    localCacheKey: out localCacheKey,
                    errorMessage: out error))
                {
                    this.FailMountAndExit($"Previous git objects root ({this.enlistment.GitObjectsRoot}) not found, and failed to determine new local cache key: {error}");
                }

                EventMetadata keyMetadata = new EventMetadata();
                keyMetadata.Add("localCacheRoot", this.enlistment.LocalCacheRoot);
                keyMetadata.Add("localCacheKey", localCacheKey);
                keyMetadata.Add(TracingConstants.MessageKey.InfoMessage, "Initializing and persisting updated paths");
                this.tracer.RelatedEvent(EventLevel.Informational, "EnsureLocalCacheIsHealthy_InitializePathsFromKey", keyMetadata);
                this.enlistment.InitializeCachePathsFromKey(this.enlistment.LocalCacheRoot, localCacheKey);

                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating GitObjectsRoot ({this.enlistment.GitObjectsRoot}), GitPackRoot ({this.enlistment.GitPackRoot}), and BlobSizesRoot ({this.enlistment.BlobSizesRoot})");
                try
                {
                    Directory.CreateDirectory(this.enlistment.GitObjectsRoot);
                    Directory.CreateDirectory(this.enlistment.GitPackRoot);
                }
                catch (Exception e)
                {
                    EventMetadata exceptionMetadata = new EventMetadata();
                    exceptionMetadata.Add("Exception", e.ToString());
                    exceptionMetadata.Add("enlistment.GitObjectsRoot", this.enlistment.GitObjectsRoot);
                    exceptionMetadata.Add("enlistment.GitPackRoot", this.enlistment.GitPackRoot);
                    this.tracer.RelatedError(exceptionMetadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to create objects and pack folders");
                    this.FailMountAndExit("Failed to create objects and pack folders");
                }

                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating new alternates file");
                if (!this.TryCreateAlternatesFile(fileSystem, out error))
                {
                    this.FailMountAndExit($"Failed to update alternates file with new objects path: {error}");
                }

                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Saving git objects root ({this.enlistment.GitObjectsRoot}) in repo metadata");
                RepoMetadata.Instance.SetGitObjectsRoot(this.enlistment.GitObjectsRoot);

                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Saving blob sizes root ({this.enlistment.BlobSizesRoot}) in repo metadata");
                RepoMetadata.Instance.SetBlobSizesRoot(this.enlistment.BlobSizesRoot);
            }

            if (!Directory.Exists(this.enlistment.BlobSizesRoot))
            {
                this.tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: BlobSizesRoot ({this.enlistment.BlobSizesRoot}) not found, re-creating");
                try
                {
                    Directory.CreateDirectory(this.enlistment.BlobSizesRoot);
                }
                catch (Exception e)
                {
                    EventMetadata exceptionMetadata = new EventMetadata();
                    exceptionMetadata.Add("Exception", e.ToString());
                    exceptionMetadata.Add("enlistment.BlobSizesRoot", this.enlistment.BlobSizesRoot);
                    this.tracer.RelatedError(exceptionMetadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to create blob sizes folder");
                    this.FailMountAndExit("Failed to create blob sizes folder");
                }
            }
        }

        private bool TryCreateAlternatesFile(PhysicalFileSystem fileSystem, out string errorMessage)
        {
            try
            {
                string alternatesFilePath = Path.Combine(this.enlistment.DotGitRoot, GVFSConstants.DotGit.Objects.Info.AlternatesRelativePath);
                string tempFilePath= alternatesFilePath + ".tmp";
                fileSystem.WriteAllText(tempFilePath, this.enlistment.GitObjectsRoot);
                fileSystem.MoveAndOverwriteFile(tempFilePath, alternatesFilePath);
            }
            catch (SecurityException e) { errorMessage = e.Message; return false; }
            catch (IOException e) { errorMessage = e.Message; return false; }

            errorMessage = null;
            return true;
        }

        private bool TrySetRequiredGitConfigSettings()
        {
            Dictionary<string, string> requiredSettings = RequiredGitConfig.GetRequiredSettings(this.enlistment);

            GitProcess git = new GitProcess(this.enlistment);

            Dictionary<string, GitConfigSetting> existingConfigSettings;
            if (!git.TryGetAllConfig(localOnly: true, configSettings: out existingConfigSettings))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> setting in requiredSettings)
            {
                GitConfigSetting existingSetting;
                if (setting.Value != null)
                {
                    if (!existingConfigSettings.TryGetValue(setting.Key, out existingSetting) ||
                        !existingSetting.HasValue(setting.Value))
                    {
                        GitProcess.Result setConfigResult = git.SetInLocalConfig(setting.Key, setting.Value);
                        if (setConfigResult.ExitCodeIsFailure)
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (existingConfigSettings.TryGetValue(setting.Key, out existingSetting))
                    {
                        git.DeleteFromLocalConfig(setting.Key);
                    }
                }
            }

            return true;
        }

        private void LogEnlistmentInfoAndSetConfigValues()
        {
            string mountId = Guid.NewGuid().ToString("N");
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);
            metadata.Add(nameof(mountId), mountId);
            metadata.Add("Enlistment", this.enlistment);
            metadata.Add("PhysicalDiskInfo", GVFSPlatform.Instance.GetPhysicalDiskInfo(this.enlistment.WorkingDirectoryRoot, sizeStatsOnly: false));
            this.tracer.RelatedEvent(EventLevel.Informational, "EnlistmentInfo", metadata, Keywords.Telemetry);

            GitProcess git = new GitProcess(this.enlistment);
            GitProcess.Result configResult = git.SetInLocalConfig(GVFSConstants.GitConfig.EnlistmentId, RepoMetadata.Instance.EnlistmentId, replaceAll: true);
            if (configResult.ExitCodeIsFailure)
            {
                string error = "Could not update config with enlistment id, error: " + configResult.Errors;
                this.tracer.RelatedWarning(error);
            }

            configResult = git.SetInLocalConfig(GVFSConstants.GitConfig.MountId, mountId, replaceAll: true);
            if (configResult.ExitCodeIsFailure)
            {
                string error = "Could not update config with mount id, error: " + configResult.Errors;
                this.tracer.RelatedWarning(error);
            }
        }

        private void UnmountAndStopWorkingDirectoryCallbacks(bool willRemountInSameProcess = false)
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

            if (!willRemountInSameProcess)
            {
                this.context?.Dispose();
                this.context = null;
            }
        }
    }
}