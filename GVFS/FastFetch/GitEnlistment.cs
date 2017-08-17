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
                  Path.Combine(repoRoot, GVFSConstants.DotGit.Objects.Root),
                  null,
                  gitBinPath, 
                  gvfsHooksRoot: null)
        {
        }

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
