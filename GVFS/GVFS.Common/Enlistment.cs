using GVFS.Common.Git;
using System;
using System.IO;

namespace GVFS.Common
{
    public abstract class Enlistment
    {
        private const string DeprecatedObjectsEndpointGitConfigName = "gvfs.objects-endpoint";
        private const string CacheEndpointGitConfigSuffix = ".cache-server-url";
        
        protected Enlistment(
            string enlistmentRoot,
            string workingDirectoryRoot,
            string repoUrl,
            string gitBinPath,
            string gvfsHooksRoot)
        {
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                throw new ArgumentException("Path to git.exe must be set");
            }

            this.EnlistmentRoot = enlistmentRoot;
            this.WorkingDirectoryRoot = workingDirectoryRoot;
            this.DotGitRoot = Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Root);
            this.GitBinPath = gitBinPath;
            this.GVFSHooksRoot = gvfsHooksRoot;

            if (repoUrl != null)
            {
                this.RepoUrl = repoUrl;
            }
            else
            {
                GitProcess.Result originResult = new GitProcess(this).GetOriginUrl();
                if (originResult.HasErrors)
                {
                    if (originResult.Errors.Length == 0)
                    {
                        throw new InvalidRepoException("Could not get origin url. remote 'origin' is not configured for this repo.'");
                    }

                    throw new InvalidRepoException("Could not get origin url. git error: " + originResult.Errors);
                }

                this.RepoUrl = originResult.Output.Trim();
            }
            
            this.Authentication = new GitAuthentication(this);
        }

        public string EnlistmentRoot { get; }
        public string WorkingDirectoryRoot { get; }
        public string DotGitRoot { get; private set; }
        public abstract string GitObjectsRoot { get; }
        public abstract string GitPackRoot { get; }
        public string RepoUrl { get; }

        public string GitBinPath { get; }
        public string GVFSHooksRoot { get; }

        public GitAuthentication Authentication { get; }

        public static string GetNewLogFileName(string logsRoot, string prefix)
        {
            if (!Directory.Exists(logsRoot))
            {
                Directory.CreateDirectory(logsRoot);
            }

            string name = prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fullPath = Path.Combine(
                logsRoot,
                name + ".log");

            if (File.Exists(fullPath))
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
    }
}