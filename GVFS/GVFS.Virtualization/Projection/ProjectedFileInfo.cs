using GVFS.Common.Git;

namespace GVFS.Virtualization.Projection
{
    public class ProjectedFileInfo
    {
        public ProjectedFileInfo(string name, long size, bool isFolder, Sha1Id sha)
        {
            this.Name = name;
            this.Size = size;
            this.IsFolder = isFolder;
            this.Sha = sha;
        }

        public string Name { get; }
        public long Size { get; }
        public bool IsFolder { get; }

        public Sha1Id Sha { get; }
    }
}
