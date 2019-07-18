using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    public class EnlistmentPathData
    {
        public List<string> GitFolderPaths;
        public List<string> GitFilePaths;
        public List<string> PlaceholderFolderPaths;
        public List<string> PlaceholderFilePaths;
        public List<string> ModifiedFolderPaths;
        public List<string> ModifiedFilePaths;

        public EnlistmentPathData()
        {
            this.GitFolderPaths = new List<string>();
            this.GitFilePaths = new List<string>();
            this.PlaceholderFolderPaths = new List<string>();
            this.PlaceholderFilePaths = new List<string>();
            this.ModifiedFolderPaths = new List<string>();
            this.ModifiedFilePaths = new List<string>();
        }

        public void AddGitTrackingPaths(IEnumerable<string> gitTrackingPaths)
        {
            // Ensure that both lists of paths have been normalized before unioning to ensure no duplicity
            List<string> normalizedGitTrackingPaths = gitTrackingPaths.ToList();
            this.NormalizePaths(normalizedGitTrackingPaths);

            this.NormalizePaths(this.ModifiedFilePaths);

            this.ModifiedFilePaths = this.ModifiedFilePaths.Union(normalizedGitTrackingPaths).ToList();
        }

        public void NormalizeAllPaths()
        {
            this.NormalizePaths(this.GitFolderPaths);
            this.NormalizePaths(this.GitFilePaths);
            this.NormalizePaths(this.PlaceholderFolderPaths);
            this.NormalizePaths(this.PlaceholderFilePaths);
            this.NormalizePaths(this.ModifiedFolderPaths);
            this.NormalizePaths(this.ModifiedFilePaths);
        }

        private void NormalizePaths(List<string> paths)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                paths[i] = paths[i].Replace(GVFSPlatform.GVFSPlatformConstants.PathSeparator, GVFSConstants.GitPathSeparator);
                paths[i] = paths[i].Trim(GVFSConstants.GitPathSeparator);
            }
        }
    }
}
