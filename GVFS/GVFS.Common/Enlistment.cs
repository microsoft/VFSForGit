using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using System;
using System.IO;

namespace GVFS.Common
{
    public abstract class Enlistment
    {
        protected Enlistment(
            string enlistmentRoot,
            string workingDirectoryRoot,
            string workingDirectoryBackingRoot,
            string repoUrl,
            string gitBinPath,
            bool flushFileBuffersForPacks,
            GitAuthentication authentication)
        {
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                throw new ArgumentException("Path to git.exe must be set");
            }

            this.EnlistmentRoot = enlistmentRoot;
            this.WorkingDirectoryRoot = workingDirectoryRoot;
            this.WorkingDirectoryBackingRoot = workingDirectoryBackingRoot;
            this.DotGitRoot = Path.Combine(this.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Root);
            this.GitBinPath = gitBinPath;
            this.FlushFileBuffersForPacks = flushFileBuffersForPacks;

            GitProcess gitProcess = new GitProcess(this);
            if (repoUrl != null)
            {
                this.RepoUrl = repoUrl;
            }
            else
            {
                GitProcess.ConfigResult originResult = gitProcess.GetOriginUrl();
                if (!originResult.TryParseAsString(out string originUrl, out string error))
                {
                    throw new InvalidRepoException("Could not get origin url. git error: " + error);
                }

                if (originUrl == null)
                {
                    throw new InvalidRepoException("Could not get origin url. remote 'origin' is not configured for this repo.'");
                }

                this.RepoUrl = originUrl.Trim();
            }

            this.Authentication = authentication ?? new GitAuthentication(gitProcess, this.RepoUrl);
        }

        public string EnlistmentRoot { get; }

        // Path to the root of the working (i.e. "src") directory.
        // On platforms where the contents of the working directory are stored
        // at a different location (e.g. Linux), WorkingDirectoryBackingRoot is the path of that backing
        // storage location.  On all other platforms WorkingDirectoryRoot and WorkingDirectoryBackingRoot
        // are the same.
        public string WorkingDirectoryRoot { get; }
        public string WorkingDirectoryBackingRoot { get; }

        public string DotGitRoot { get; private set; }
        public abstract string GitObjectsRoot { get; protected set; }
        public abstract string LocalObjectsRoot { get; protected set; }
        public abstract string GitPackRoot { get; protected set; }
        public string RepoUrl { get; }
        public bool FlushFileBuffersForPacks { get; }

        public string GitBinPath { get; }

        public GitAuthentication Authentication { get; }

        public static string GetNewLogFileName(
            string logsRoot,
            string prefix,
            string logId = null,
            PhysicalFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new PhysicalFileSystem();

            // TODO: Remove Directory.CreateDirectory() code from here
            // Don't change the state from an accessor.
            if (!fileSystem.DirectoryExists(logsRoot))
            {
                fileSystem.CreateDirectory(logsRoot);
            }

            logId = logId ?? DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string name = prefix + "_" + logId;
            string fullPath = Path.Combine(
                logsRoot,
                name + ".log");

            if (fileSystem.FileExists(fullPath))
            {
                fullPath = Path.Combine(
                    logsRoot,
                    name + "_" + Guid.NewGuid().ToString("N") + ".log");
            }

            return fullPath;
        }

        public virtual GitProcess CreateGitProcess()
        {
            return new GitProcess(this);
        }

        public bool GetTrustPackIndexesConfig()
        {
            var gitProcess = this.CreateGitProcess();
            bool trustPackIndexes = true;
            if (gitProcess.TryGetFromConfig(GVFSConstants.GitConfig.TrustPackIndexes, forceOutsideEnlistment: false, out var valueString)
                && bool.TryParse(valueString, out var trustPackIndexesConfig))
            {
                trustPackIndexes = trustPackIndexesConfig;
            }

            return trustPackIndexes;
        }
    }
}
