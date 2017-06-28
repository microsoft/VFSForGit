using GVFS.Common.Git;
using System;
using System.IO;
using System.Threading;

namespace GVFS.Common
{
    public class GVFSEnlistment : Enlistment
    {
        // New enlistment
        public GVFSEnlistment(string enlistmentRoot, string repoUrl, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
            : base(
                  enlistmentRoot, 
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName),
                  Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.GitObjectCachePath),
                  repoUrl, 
                  cacheServerUrl, 
                  gitBinPath, 
                  gvfsHooksRoot)
        {
            this.NamedPipeName = NamedPipes.NamedPipeClient.GetPipeNameFromPath(this.EnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.Root);
            this.GVFSLogsRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.LogPath);

            // Mutex name cannot include '\' (other than the '\' after Global)
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682411(v=vs.85).aspx 
            this.EnlistmentMutex = new Mutex(false, "Global\\" + this.NamedPipeName.Replace('\\', ':'));
        }

        // Enlistment without repo url. This skips git commands that may fail in a corrupt repo.
        public GVFSEnlistment(string enlistmentRoot, string gitBinPath)
            : this(
                  enlistmentRoot,
                  repoUrl: "invalid://repoUrl",
                  cacheServerUrl: "invalid://cacheServerUrl",
                  gitBinPath: gitBinPath,
                  gvfsHooksRoot: null)
        {
        }

        // Existing, configured enlistment
        public GVFSEnlistment(string enlistmentRoot, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
            : this(
                  enlistmentRoot, 
                  null,
                  cacheServerUrl, 
                  gitBinPath, 
                  gvfsHooksRoot)
        {
        }

        public Mutex EnlistmentMutex { get; }

        public string NamedPipeName { get; private set; }

        public string DotGVFSRoot { get; private set; }

        public string GVFSLogsRoot { get; private set; }

        public static GVFSEnlistment CreateFromCurrentDirectory(string cacheServerUrl, string gitBinRoot)
        {
            return CreateFromDirectory(Environment.CurrentDirectory, cacheServerUrl, gitBinRoot, null);
        }

        public static GVFSEnlistment CreateWithoutRepoUrlFromDirectory(string directory, string gitBinRoot)
        {
            if (Directory.Exists(directory))
            {
                string enlistmentRoot = EnlistmentUtils.GetEnlistmentRoot(directory);
                if (enlistmentRoot != null)
                {
                    return new GVFSEnlistment(enlistmentRoot, gitBinRoot);
                }
            }

            return null;
        }

        public static GVFSEnlistment CreateFromDirectory(string directory, string cacheServerUrl, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string enlistmentRoot = EnlistmentUtils.GetEnlistmentRoot(directory);
                if (enlistmentRoot != null)
                {
                    return new GVFSEnlistment(enlistmentRoot, cacheServerUrl, gitBinRoot, gvfsHooksRoot);
                }
            }

            return null;
        }

        public static string ToFullPath(string originalValue, string toUseIfOriginalNullOrWhitespace)
        {
            if (string.IsNullOrWhiteSpace(originalValue))
            {
                return toUseIfOriginalNullOrWhitespace;
            }

            try
            {
                return Path.GetFullPath(originalValue);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetNewGVFSLogFileName(string logsRoot, string logFileType)
        {
            return Enlistment.GetNewLogFileName(
                logsRoot, 
                "gvfs_" + logFileType);
        }

        public bool TrySetCacheServerUrlConfig()
        {
            GitProcess git = new Git.GitProcess(this);
            string settingName = Enlistment.GetCacheConfigSettingName(this.RepoUrl);
            return !git.SetInLocalConfig(settingName, this.CacheServerUrl).HasErrors;
        }

        public bool TryCreateEnlistmentFolders()
        {
            try
            {
                Directory.CreateDirectory(this.EnlistmentRoot);
                Directory.CreateDirectory(this.WorkingDirectoryRoot);
                this.CreateHiddenDirectory(this.DotGVFSRoot);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }

        public bool TryConfigureAlternate(out string errorMessage)
        {
            try
            {
                if (!Directory.Exists(this.GitObjectsRoot))
                {
                    Directory.CreateDirectory(this.GitObjectsRoot);
                    Directory.CreateDirectory(this.GitPackRoot);
                }

                File.WriteAllText(
                    Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Info.Alternates),
                    @"..\..\..\" + GVFSConstants.DotGVFS.GitObjectCachePath);
            }
            catch (IOException e)
            {
                errorMessage = e.Message;
                return false;
            }

            errorMessage = null;
            return true;
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
