using System;

namespace GVFS.Common.Prefetch.Git
{
    public class PathWithMode
    {
        public PathWithMode(string path, ushort mode)
        {
            this.Path = path;
            this.Mode = mode;
        }

        public ushort Mode { get; }
        public string Path { get; }

        public override bool Equals(object obj)
        {
            PathWithMode x = obj as PathWithMode;

            if (x == null)
            {
                return false;
            }

            return x.Path.Equals(this.Path, GVFSPlatform.Instance.Constants.PathComparison);
        }

        public override int GetHashCode()
        {
            return  GVFSPlatform.Instance.Constants.PathComparer.GetHashCode(this.Path);
        }
    }
}
