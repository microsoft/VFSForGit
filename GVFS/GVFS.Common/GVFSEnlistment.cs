using GVFS.Common.NamedPipes;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
        public GVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, string gvfsHooksRoot)
            : base(
                  enlistmentRoot,
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName),
                  repoUrl,
                  gitBinPath,
                  gvfsHooksRoot,
                  flushFileBuffersForPacks: true)
        {
            this.NamedPipeName = GVFSPlatform.Instance.GetNamedPipeName(this.EnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.Root);
            this.GitStatusCacheFolder = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.Name);
            this.GitStatusCachePath = Path.Combine(this.DotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.CachePath);
            this.GVFSLogsRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.LogPath);
            this.LocalObjectsRoot = Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Root);
        }

        // Existing, configured enlistment
        private GVFSEnlistment(string enlistmentRoot, string gitBinPath, string gvfsHooksRoot)
            : this(
                  enlistmentRoot,
                  null,
                  gitBinPath,
                  gvfsHooksRoot)
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

        public static GVFSEnlistment CreateWithoutRepoUrlFromDirectory(string directory, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string errorMessage;
                string enlistmentRoot;
                if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(directory, out enlistmentRoot, out errorMessage))
                {
                    return null;
                }

                return new GVFSEnlistment(enlistmentRoot, string.Empty, gitBinRoot, gvfsHooksRoot);
            }

            return null;
        }

        public static GVFSEnlistment CreateFromDirectory(string directory, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string errorMessage;
                string enlistmentRoot;
                if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(directory, out enlistmentRoot, out errorMessage))
                {
                    return null;
                }

                return new GVFSEnlistment(enlistmentRoot, gitBinRoot, gvfsHooksRoot);
            }

            return null;
        }

        public static string GetNewGVFSLogFileName(string logsRoot, string logFileType)
        {
            return Enlistment.GetNewLogFileName(
                logsRoot, 
                "gvfs_" + logFileType);
        }

        public static bool WaitUntilMounted(string enlistmentRoot, bool unattended, out string errorMessage)
        {
            errorMessage = null;
            using (NamedPipeClient pipeClient = new NamedPipeClient(GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot)))
            {
                int timeout = unattended ? 300000 : 60000;
                if (!pipeClient.Connect(timeout))
                {
                    errorMessage = "Unable to mount because the GVFS.Mount process is not responding.";
                    return false;
                }

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
                            return true;
                        }
                        else if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.MountFailed)
                        {
                            errorMessage = string.Format("Failed to mount at {0}", enlistmentRoot);
                            return false;
                        }
                        else
                        {
                            Thread.Sleep(500);
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        errorMessage = string.Format("Could not connect to GVFS.Mount: {0}", e);
                        return false;
                    }
                    catch (JsonReaderException e)
                    {
                        errorMessage = string.Format("Failed to parse response from GVFS.Mount.\n {0}", e);
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

        public bool TryCreateEnlistmentFolders()
        {
            try
            {
                Directory.CreateDirectory(this.EnlistmentRoot);
                GVFSPlatform.Instance.InitializeEnlistmentACLs(this.EnlistmentRoot);
                Directory.CreateDirectory(this.WorkingDirectoryRoot);
                this.CreateHiddenDirectory(this.DotGVFSRoot);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
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
    }
}
