using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GVFS.Common
{
    public partial class GVFSEnlistment : Enlistment
    {
        public const string BlobSizesCacheName = "blobSizes";

        private const string GitObjectCacheName = "gitObjects";

        private string gitVersion;
        private string gvfsVersion;
        private string gvfsHooksVersion;

        // New enlistment
        public GVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, GitAuthentication authentication)
            : base(
                  enlistmentRoot,
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName),
                  Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.WorkingDirectoryBackingRootPath),
                  repoUrl,
                  gitBinPath,
                  flushFileBuffersForPacks: true,
                  authentication: authentication)
        {
            this.NamedPipeName = GVFSPlatform.Instance.GetNamedPipeName(this.PrimaryEnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.PrimaryEnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            this.GitStatusCacheFolder = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.Name);
            this.GitStatusCachePath = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.CachePath);
            this.GVFSLogsRoot = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.LogName);
            this.LocalObjectsRoot = Path.Combine(this.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Objects.Root);
        }

        // Existing, configured enlistment
        private GVFSEnlistment(string enlistmentRoot, string gitBinPath, GitAuthentication authentication)
            : this(
                  enlistmentRoot,
                  null,
                  gitBinPath,
                  authentication)
        {
        }

        // Worktree enlistment — overrides working directory, pipe name, and metadata paths
        private GVFSEnlistment(string enlistmentRoot, string gitBinPath, GitAuthentication authentication, WorktreeInfo worktreeInfo, string repoUrl = null)
            : base(
                  enlistmentRoot,
                  worktreeInfo.WorktreePath,
                  worktreeInfo.WorktreePath,
                  repoUrl,
                  gitBinPath,
                  flushFileBuffersForPacks: true,
                  authentication: authentication)
        {
            this.Worktree = worktreeInfo;

            // Override DotGitRoot to point to the shared .git directory.
            // The base constructor sets it to WorkingDirectoryBackingRoot/.git
            // which is a file (not directory) in worktrees.
            this.DotGitRoot = worktreeInfo.SharedGitDir;

            this.DotGVFSRoot = Path.Combine(worktreeInfo.WorktreeGitDir, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            this.NamedPipeName = GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot) + worktreeInfo.PipeSuffix;
            this.GitStatusCacheFolder = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.Name);
            this.GitStatusCachePath = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.CachePath);
            this.GVFSLogsRoot = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.LogName);
            this.LocalObjectsRoot = Path.Combine(worktreeInfo.SharedGitDir, "objects");
        }

        public string NamedPipeName { get; }

        public string DotGVFSRoot { get; }

        public string GVFSLogsRoot { get; }

        public WorktreeInfo Worktree { get; }

        public bool IsWorktree => this.Worktree != null;

        /// <summary>
        /// Path to the git index file. For worktrees this is in the
        /// per-worktree git dir, not in the working directory.
        /// </summary>
        public override string GitIndexPath
        {
            get
            {
                if (this.IsWorktree)
                {
                    return Path.Combine(this.Worktree.WorktreeGitDir, GVFSConstants.DotGit.IndexName);
                }

                return base.GitIndexPath;
            }
        }

        public string LocalCacheRoot { get; private set; }

        public string BlobSizesRoot { get; private set; }

        public override string GitObjectsRoot { get; protected set; }
        public override string LocalObjectsRoot { get; protected set; }
        public override string GitPackRoot { get; protected set; }
        public string GitStatusCacheFolder { get; private set; }
        public string GitStatusCachePath { get; private set; }

        // These version properties are only used in logging during clone and mount to track version numbers
        public string GitVersion
        {
            get { return this.gitVersion; }
        }

        public string GVFSVersion
        {
            get { return this.gvfsVersion; }
        }

        public string GVFSHooksVersion
        {
            get { return this.gvfsHooksVersion; }
        }

        public static GVFSEnlistment CreateFromDirectory(
            string directory,
            string gitBinRoot,
            GitAuthentication authentication,
            bool createWithoutRepoURL = false)
        {
            if (Directory.Exists(directory))
            {
                // Always check for worktree first. A worktree directory may
                // be under the enlistment tree, so TryGetGVFSEnlistmentRoot
                // can succeed by walking up — but we need a worktree enlistment.
                string worktreeError;
                WorktreeInfo wtInfo = TryGetWorktreeInfo(directory, out worktreeError);
                if (worktreeError != null)
                {
                    throw new InvalidRepoException($"Failed to check worktree status for '{directory}': {worktreeError}");
                }

                if (wtInfo?.SharedGitDir != null)
                {
                    string primaryRoot = wtInfo.GetEnlistmentRoot();
                    if (primaryRoot != null)
                    {
                        // Read origin URL via the shared .git dir (not the worktree's
                        // .git file) because the base Enlistment constructor runs
                        // git config before we can override DotGitRoot.
                        string srcDir = Path.GetDirectoryName(wtInfo.SharedGitDir);
                        string repoUrl = null;
                        if (srcDir != null)
                        {
                            GitProcess git = new GitProcess(gitBinRoot, srcDir);
                            GitProcess.ConfigResult urlResult = git.GetOriginUrl();
                            urlResult.TryParseAsString(out repoUrl, out _);
                        }

                        return CreateForWorktree(primaryRoot, gitBinRoot, authentication, wtInfo, repoUrl?.Trim());
                    }
                }

                string errorMessage;
                string enlistmentRoot;
                if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(directory, out enlistmentRoot, out errorMessage))
                {
                    throw new InvalidRepoException($"Could not get enlistment root. Error: {errorMessage}");
                }

                if (createWithoutRepoURL)
                {
                    return new GVFSEnlistment(enlistmentRoot, string.Empty, gitBinRoot, authentication);
                }

                return new GVFSEnlistment(enlistmentRoot, gitBinRoot, authentication);
            }

            throw new InvalidRepoException($"Directory '{directory}' does not exist");
        }

        /// <summary>
        /// Creates a GVFSEnlistment for a git worktree. Uses the primary
        /// enlistment root for shared config but maps working directory,
        /// metadata, and pipe name to the worktree.
        /// </summary>
        public static GVFSEnlistment CreateForWorktree(
            string primaryEnlistmentRoot,
            string gitBinRoot,
            GitAuthentication authentication,
            WorktreeInfo worktreeInfo,
            string repoUrl = null)
        {
            return new GVFSEnlistment(primaryEnlistmentRoot, gitBinRoot, authentication, worktreeInfo, repoUrl);
        }

        public static string GetNewGVFSLogFileName(
            string logsRoot,
            string logFileType,
            string logId = null,
            PhysicalFileSystem fileSystem = null)
        {
            return Enlistment.GetNewLogFileName(
                logsRoot,
                "gvfs_" + logFileType,
                logId: logId,
                fileSystem: fileSystem);
        }

        public static bool WaitUntilMounted(ITracer tracer, string enlistmentRoot, bool unattended, out string errorMessage)
        {
            string pipeName = GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot);
            return WaitUntilMounted(tracer, pipeName, enlistmentRoot, unattended, out errorMessage);
        }

        public static bool WaitUntilMounted(ITracer tracer, string pipeName, string enlistmentRoot, bool unattended, out string errorMessage)
        {
            return WaitUntilMounted(tracer, pipeName, enlistmentRoot, unattended, mountProcessStatus: null, out errorMessage);
        }

        /// <summary>
        /// Waits for the GVFS.Mount process to come up and signal readiness
        /// over its named pipe.
        /// </summary>
        /// <param name="mountProcessStatus">
        /// Optional snapshot delegate. When provided, the wait loop polls it
        /// during pipe-connection attempts so the caller can fail fast if the
        /// mount process exits before the pipe is created — instead of
        /// blocking on the full 60-second pipe timeout. Callers that don't
        /// have a handle to the child process (e.g. clients connecting to
        /// somebody else's mount) pass <c>null</c>.
        /// </param>
        public static bool WaitUntilMounted(
            ITracer tracer,
            string pipeName,
            string enlistmentRoot,
            bool unattended,
            Func<MountProcessSnapshot> mountProcessStatus,
            out string errorMessage)
        {
            tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Creating NamedPipeClient for pipe '{pipeName}'");
            tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Connecting to '{pipeName}'");

            errorMessage = null;
            int totalTimeoutMs = unattended ? 300000 : 60000;
            NamedPipeClient pipeClient = TryConnectWithProcessTracking(
                tracer,
                pipeName,
                totalTimeoutMs,
                mountProcessStatus,
                out errorMessage);
            if (pipeClient == null)
            {
                return false;
            }

            using (pipeClient)
            {
                tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Connected to '{pipeName}'");

                while (true)
                {
                    string response = string.Empty;
                    try
                    {
                        pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                        response = pipeClient.ReadRawResponse();
                        NamedPipeMessages.GetStatus.Response getStatusResponse =
                            NamedPipeMessages.GetStatus.Response.FromJson(response);

                        if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.Ready)
                        {
                            tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Mount process ready");
                            return true;
                        }
                        else if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.MountFailed)
                        {
                            errorMessage = string.Format("Failed to mount at {0}", enlistmentRoot);
                            tracer.RelatedError($"{nameof(WaitUntilMounted)}: Mount failed: {errorMessage}");
                            return false;
                        }
                        else
                        {
                            tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Waiting 500ms for mount process to be ready");
                            Thread.Sleep(100);
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        errorMessage = string.Format("Could not connect to GVFS.Mount: {0}", e);
                        tracer.RelatedError($"{nameof(WaitUntilMounted)}: {errorMessage}");
                        return false;
                    }
                    catch (JsonException e)
                    {
                        errorMessage = string.Format("Failed to parse response from GVFS.Mount.\n {0}", e);
                        tracer.RelatedError($"{nameof(WaitUntilMounted)}: {errorMessage}");
                        return false;
                    }
                }
            }
        }

        private static NamedPipeClient TryConnectWithProcessTracking(
            ITracer tracer,
            string pipeName,
            int totalTimeoutMs,
            Func<MountProcessSnapshot> mountProcessStatus,
            out string errorMessage)
        {
            errorMessage = null;

            // When no process snapshot is supplied, fall back to a single
            // long-timeout connect to preserve previous behavior for callers
            // that don't own the mount process (e.g. external pipe clients).
            if (mountProcessStatus == null)
            {
                NamedPipeClient pipeClient = new NamedPipeClient(pipeName);
                if (pipeClient.Connect(totalTimeoutMs))
                {
                    return pipeClient;
                }

                pipeClient.Dispose();
                tracer.RelatedError($"{nameof(WaitUntilMounted)}: Failed to connect to '{pipeName}' after {totalTimeoutMs} ms");
                errorMessage = "Unable to mount because the GVFS.Mount process is not responding.";
                return null;
            }

            // With process tracking, retry with short connect attempts so we
            // can detect early termination within seconds.
            const int PerAttemptTimeoutMs = 500;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(totalTimeoutMs);
            while (true)
            {
                MountProcessSnapshot snapshot = mountProcessStatus();
                if (snapshot.HasExited)
                {
                    errorMessage = string.Format(
                        "GVFS.Mount process (Id {0}) exited with code {1} before the named pipe was ready.",
                        snapshot.ProcessId,
                        snapshot.ExitCode);
                    tracer.RelatedError($"{nameof(WaitUntilMounted)}: {errorMessage}");
                    return null;
                }

                NamedPipeClient pipeClient = new NamedPipeClient(pipeName);
                if (pipeClient.Connect(PerAttemptTimeoutMs))
                {
                    return pipeClient;
                }

                pipeClient.Dispose();

                if (DateTime.UtcNow >= deadline)
                {
                    tracer.RelatedError($"{nameof(WaitUntilMounted)}: Failed to connect to '{pipeName}' after {totalTimeoutMs} ms (mount process Id {snapshot.ProcessId} still running)");
                    errorMessage = "Unable to mount because the GVFS.Mount process is not responding.";
                    return null;
                }
            }
        }

        /// <summary>
        /// Snapshot of a child mount process's liveness, used by
        /// <see cref="WaitUntilMounted(ITracer, string, string, bool, Func{MountProcessSnapshot}, out string)"/>
        /// to short-circuit the pipe-wait when the child has crashed.
        /// </summary>
        public readonly struct MountProcessSnapshot
        {
            public MountProcessSnapshot(int processId, bool hasExited, int exitCode)
            {
                this.ProcessId = processId;
                this.HasExited = hasExited;
                this.ExitCode = exitCode;
            }

            public int ProcessId { get; }
            public bool HasExited { get; }
            public int ExitCode { get; }
        }


        public void SetGitVersion(string gitVersion)
        {
            this.SetOnce(gitVersion, ref this.gitVersion);
        }

        public void SetGVFSVersion(string gvfsVersion)
        {
            this.SetOnce(gvfsVersion, ref this.gvfsVersion);
        }

        public void SetGVFSHooksVersion(string gvfsHooksVersion)
        {
            this.SetOnce(gvfsHooksVersion, ref this.gvfsHooksVersion);
        }

        public void InitializeCachePathsFromKey(string localCacheRoot, string localCacheKey)
        {
            this.InitializeCachePaths(
                localCacheRoot,
                Path.Combine(localCacheRoot, localCacheKey, GitObjectCacheName),
                Path.Combine(localCacheRoot, localCacheKey, BlobSizesCacheName));
        }

        public void InitializeCachePaths(string localCacheRoot, string gitObjectsRoot, string blobSizesRoot)
        {
            this.LocalCacheRoot = localCacheRoot;
            this.GitObjectsRoot = gitObjectsRoot;
            this.GitPackRoot = Path.Combine(this.GitObjectsRoot, GVFSConstants.DotGit.Objects.Pack.Name);
            this.BlobSizesRoot = blobSizesRoot;
        }

        public bool TryCreateEnlistmentSubFolders()
        {
            try
            {
                GVFSPlatform.Instance.FileSystem.EnsureDirectoryIsOwnedByCurrentUser(this.WorkingDirectoryRoot);
                this.CreateHiddenDirectory(this.DotGVFSRoot);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }

        public string GetMountId()
        {
            return this.GetId(GVFSConstants.GitConfig.MountId);
        }

        public string GetEnlistmentId()
        {
            return this.GetId(GVFSConstants.GitConfig.EnlistmentId);
        }

        private void SetOnce<T>(T value, ref T valueToSet)
        {
            if (valueToSet != null)
            {
                throw new InvalidOperationException("Value already set.");
            }

            valueToSet = value;
        }

        /// <summary>
        /// Creates a hidden directory @ the given path.
        /// If directory already exists, hides it.
        /// </summary>
        /// <param name="path">Path to desired hidden directory</param>
        private void CreateHiddenDirectory(string path)
        {
            DirectoryInfo dir = Directory.CreateDirectory(path);
            dir.Attributes = FileAttributes.Hidden;
        }

        private string GetId(string key)
        {
            GitProcess.ConfigResult configResult = this.CreateGitProcess().GetFromLocalConfig(key);
            string value;
            string error;
            configResult.TryParseAsString(out value, out error, defaultValue: string.Empty);
            return value.Trim();
        }
    }
}
