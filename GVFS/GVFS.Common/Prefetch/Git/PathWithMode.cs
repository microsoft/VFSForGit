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
            if (obj == null)
            {
                return false;
            }

            PathWithMode x = obj as PathWithMode;
            if (x.Path.Equals(this.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(this.Path);
        }
    }
}
