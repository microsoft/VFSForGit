using System.Collections.Generic;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface for interacting with placeholders
    /// </summary>
    public interface IPlaceholderCollection
    {
        int GetCount();
        void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
        int GetFilePlaceholdersCount();
        int GetFolderPlaceholdersCount();

        HashSet<string> GetAllFilePaths();

        void AddPartialFolder(string path, string sha);
        void AddExpandedFolder(string path);
        void AddPossibleTombstoneFolder(string path);

        void AddFile(string path, string sha);

        void Remove(string path);
        List<IPlaceholderData> RemoveAllEntriesForFolder(string path);
        void AddPlaceholderData(IPlaceholderData data);
    }
}
