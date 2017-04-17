using GVFS.Common;
using System;
using System.IO;
using System.Linq;

namespace FastFetch
{
    public class GitEnlistment : Enlistment
    {
        private GitEnlistment(string repoRoot, string cacheBaseUrl, string gitBinPath)
            : base(repoRoot, repoRoot, cacheBaseUrl, gitBinPath, gvfsHooksRoot: null)
        {
        }

        public string FastFetchLogRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGit.Root, ".fastfetch"); }
        }
                       
        public static GitEnlistment CreateFromCurrentDirectory(string objectsEndpoint, string gitBinPath)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(Environment.CurrentDirectory);
            while (dirInfo != null && dirInfo.Exists)
            {
                DirectoryInfo[] dotGitDirs = dirInfo.GetDirectories(GVFSConstants.DotGit.Root);

                if (dotGitDirs.Count() == 1)
                {
                    return new GitEnlistment(dirInfo.FullName, objectsEndpoint, gitBinPath);
                }

                dirInfo = dirInfo.Parent;
            }

            return null;
        }
    }
}
