namespace MirrorProvider
{
    public class ProjectedFileInfo
    {
        public ProjectedFileInfo(string name, long size, bool isDirectory)
        {
            this.Name = name;
            this.Size = size;
            this.IsDirectory = isDirectory;
        }

        public string Name { get; }
        public long Size { get; }
        public bool IsDirectory { get; }
    }
}
