using System;
namespace GVFS.Virtualization.Projection
{
    public struct FileTypeAndMode
    {
        public static readonly ushort FileMode755;
        public static readonly ushort FileMode664;
        public static readonly ushort FileMode644;
        public static readonly FileTypeAndMode Regular644File;

        // Bitmasks for extracting file type and mode from the ushort stored in the index
        private const ushort FileTypeMask = 0xF000;
        private const ushort FileModeMask = 0x1FF;

        private readonly ushort fileTypeAndMode;

        static FileTypeAndMode()
        {
            FileMode755 = Convert.ToUInt16("755", 8);
            FileMode664 = Convert.ToUInt16("664", 8);
            FileMode644 = Convert.ToUInt16("644", 8);

            Regular644File = new FileTypeAndMode((ushort)((ushort)FileType.Regular | FileMode644));
        }

        public FileTypeAndMode(ushort typeAndModeInIndexFormat)
        {
            this.fileTypeAndMode = typeAndModeInIndexFormat;
        }

        public enum FileType : ushort
        {
            Invalid = 0,

            Regular = 0x8000,
            SymLink = 0xA000,
            GitLink = 0xE000,
        }

        public FileType Type => (FileType)(this.fileTypeAndMode & FileTypeMask);
        public ushort Mode => (ushort)(this.fileTypeAndMode & FileModeMask);

        public static bool operator ==(FileTypeAndMode lhs, FileTypeAndMode rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(FileTypeAndMode lhs, FileTypeAndMode rhs)
        {
            return !lhs.Equals(rhs);
        }

        public override bool Equals(object obj)
        {
            if (obj is FileTypeAndMode)
            {
                return this.Equals((FileTypeAndMode)obj);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return this.fileTypeAndMode;
        }

        public bool Equals(FileTypeAndMode otherFileTypeAndMode)
        {
            return this.fileTypeAndMode == otherFileTypeAndMode.fileTypeAndMode;
        }

        public string GetModeAsOctalString()
        {
            return Convert.ToString(this.Mode, 8);
        }
    }
}
