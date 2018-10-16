namespace MirrorProvider
{
    public class ProjectedFileInfo
    {
        public ProjectedFileInfo(string name, long size, FileType type)
        {
            this.Name = name;
            this.Size = size;
            this.Type = type;
        }

        public enum FileType
        {
            Invalid,

            File,
            Directory,
            SymLink

        }

        public string Name { get; }
        public long Size { get; }
        public FileType Type { get; }
        public bool IsDirectory => this.Type == FileType.Directory;
    }
}
