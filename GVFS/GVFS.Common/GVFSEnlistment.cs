using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.IO;
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
            this.NamedPipeName = GVFSPlatform.Instance.GetNamedPipeName(this.EnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            this.GitStatusCacheFolder = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.Name);
            this.GitStatusCachePath = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.CachePath);
            this.GVFSLogsRoot = Path.Combine(this.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, GVFSConstants.DotGVFS.LogName);
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

        public string NamedPipeName { get; }

        public string DotGVFSRoot { get; }

        public string GVFSLogsRoot { get; }

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
            tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Creating NamedPipeClient for pipe '{pipeName}'");

            errorMessage = null;
            using (NamedPipeClient pipeClient = new NamedPipeClient(pipeName))
            {
                tracer.RelatedInfo($"{nameof(WaitUntilMounted)}: Connecting to '{pipeName}'");

                int timeout = unattended ? 300000 : 60000;
                if (!pipeClient.Connect(timeout))
                {
                    tracer.RelatedError($"{nameof(WaitUntilMounted)}: Failed to connect to '{pipeName}' after {timeout} ms");
                    errorMessage = "Unable to mount because the GVFS.Mount process is not responding.";
                    return false;
                }

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
                            Thread.Sleep(500);
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        errorMessage = string.Format("Could not connect to GVFS.Mount: {0}", e);
                        tracer.RelatedError($"{nameof(WaitUntilMounted)}: {errorMessage}");
                        return false;
                    }
                    catch (JsonReaderException e)
                    {
                        errorMessage = string.Format("Failed to parse response from GVFS.Mount.\n {0}", e);
                        tracer.RelatedError($"{nameof(WaitUntilMounted)}: {errorMessage}");
                        return false;
                    }
                }
            }
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
