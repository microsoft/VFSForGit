using System.IO;

namespace GVFS.Common.FileBasedCollections
{
    public abstract class PlaceholderEvent
    {
        public const byte FilePrefix = 1 << 0;
        public const byte PartialFolderPrefix = 1 << 1;
        public const byte ExpandedFolderPrefix = 1 << 2;

        public readonly string Path;

        protected PlaceholderEvent(string path)
        {
            this.Path = path;
        }

        public abstract void Serialize(BinaryWriter writer);

        public override bool Equals(object obj)
        {
            PlaceholderEvent other = (PlaceholderEvent)obj;

            return (other.GetType(), other.Path).Equals((this.GetType(), this.Path));
        }

        // override object.GetHashCode
        public override int GetHashCode()
        {
            return (this.GetType(), this.Path).GetHashCode();
        }
    }
}
