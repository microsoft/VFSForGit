using System;
namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal struct FileTypeAndMode
        {
            // Bitmasks for extracting file type and mode from the ushort stored in the index
            private const ushort FileTypeMask = 0xF000;
            private const ushort FileModeMask = 0x1FF;

            // Values used in the index file to indicate the type of the file
            private const ushort RegularFileIndexEntry = 0x8000;
            private const ushort SymLinkFileIndexEntry = 0xA000;
            private const ushort GitLinkFileIndexEntry = 0xE000;

            public FileTypeAndMode(ushort typeAndModeInIndexFormat)
            {
                switch (typeAndModeInIndexFormat & FileTypeMask)
                {
                    case RegularFileIndexEntry:
                        this.Type = FileType.Regular;
                        break;
                    case SymLinkFileIndexEntry:
                        this.Type = FileType.SymLink;
                        break;
                    case GitLinkFileIndexEntry:
                        this.Type = FileType.GitLink;
                        break;
                    default:
                        this.Type = FileType.Invalid;
                        break;
                }

                this.Mode = (ushort)(typeAndModeInIndexFormat & FileModeMask);
            }

            public FileType Type { get; }
            public ushort Mode { get; }

            public string GetModeAsOctalString()
            {
                return Convert.ToString(this.Mode, 8);
            }
        }
    }
}
