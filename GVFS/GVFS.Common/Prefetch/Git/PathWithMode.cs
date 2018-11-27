namespace GVFS.Common.Prefetch.Git
{
    public class PathWithMode
    {
        public PathWithMode(ushort mode, string path)
        {
            this.Mode = mode;
            this.Path = path;
        }

        public ushort Mode { get; set; }
        public string Path { get; set; }
    }
}
