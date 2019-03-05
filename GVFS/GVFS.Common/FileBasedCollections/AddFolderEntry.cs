using System.IO;

namespace GVFS.Common.FileBasedCollections
{
    public class AddFolderEntry : PlaceholderEvent
    {
        public readonly bool IsExpandedFolder;

        public AddFolderEntry(string path, bool isExpandedFolder) : base(path)
        {
            this.IsExpandedFolder = isExpandedFolder;
        }

        public override void Serialize(BinaryWriter writer)
        {
            if (this.IsExpandedFolder)
            {
                writer.Write(ExpandedFolderPrefix);
            }
            else
            {
                writer.Write(PartialFolderPrefix);
            }

            writer.Write(this.Path);
        }
    }
}
