using RGFS.Common;
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
                  null,
                  gitBinPath,
                  rgfsHooksRoot: null)
        {
            this.GitObjectsRoot = Path.Combine(repoRoot, RGFSConstants.DotGit.Objects.Root);
            this.GitPackRoot = Path.Combine(this.GitObjectsRoot, RGFSConstants.DotGit.Objects.Pack.Name);
        }

        public override string GitObjectsRoot { get; }

        public override string GitPackRoot { get; }

        public string FastFetchLogRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, RGFSConstants.DotGit.Root, ".fastfetch"); }
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
