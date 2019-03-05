using System.IO;

namespace GVFS.Common.FileBasedCollections
{
    public class AddFileEntry : PlaceholderEvent
    {
        public readonly string Sha;
        public AddFileEntry(string path, string sha) : base(path)
        {
            this.Sha = sha;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(FilePrefix);
            writer.Write(this.Path);
            writer.Write(this.Sha.ToCharArray());
        }
    }
}
