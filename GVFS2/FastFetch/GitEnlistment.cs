using GVFS.Common;
using System;
using System.IO;

namespace FastFetch
{
    public class GitEnlistment : Enlistment
    {
        private GitEnlistment(string repoRoot, string gitBinPath)
            : base(
                  repoRoot,
                  repoRoot,
                  repoRoot,
                  null,
                  gitBinPath,
                  flushFileBuffersForPacks: false,
                  authentication: null)
        {
            this.GitObjectsRoot = Path.Combine(repoRoot, GVFSConstants.DotGit.Objects.Root);
            this.LocalObjectsRoot = this.GitObjectsRoot;
            this.GitPackRoot = Path.Combine(this.GitObjectsRoot, GVFSConstants.DotGit.Objects.Pack.Name);
        }

        public override string GitObjectsRoot { get; protected set; }

        public override string LocalObjectsRoot { get; protected set; }

        public override string GitPackRoot { get; protected set; }

        public string FastFetchLogRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGit.Root, ".fastfetch"); }
        }

        public static GitEnlistment CreateFromCurrentDirectory(string gitBinPath)
        {
            string root = Paths.GetGitEnlistmentRoot(Environment.CurrentDirectory);
            if (root != null)
            {
                return new GitEnlistment(root, gitBinPath);
            }

            return null;
        }
    }
}
