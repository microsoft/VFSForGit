using System.IO;

namespace GVFS.Common.FileBasedCollections
{
    public class PlaceholderRemoved : PlaceholderEvent
    {
        public PlaceholderRemoved(string path) : base(path)
        {
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(this.Path);
        }
    }
}
