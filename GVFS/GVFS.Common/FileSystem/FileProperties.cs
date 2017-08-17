using System;
using System.IO;

namespace GVFS.Common.FileSystem
{
    public class FileProperties
    {
        public static readonly FileProperties DefaultFile  = new FileProperties(FileAttributes.Normal, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, 0);
        public static readonly FileProperties DefaultDirectory = new FileProperties(FileAttributes.Directory, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, 0);

        public FileProperties(FileAttributes attributes, DateTime creationTimeUTC, DateTime lastAccessTimeUTC, DateTime lastWriteTimeUTC, long length)
        {
            this.FileAttributes = attributes;
            this.CreationTimeUTC = creationTimeUTC;
            this.LastAccessTimeUTC = lastAccessTimeUTC;
            this.LastWriteTimeUTC = lastWriteTimeUTC;
            this.Length = length;
        }

        public FileAttributes FileAttributes { get; private set; }
        public DateTime CreationTimeUTC { get; private set; }
        public DateTime LastAccessTimeUTC { get; private set; }
        public DateTime LastWriteTimeUTC { get; private set; }
        public long Length { get; private set; }
    }
}
