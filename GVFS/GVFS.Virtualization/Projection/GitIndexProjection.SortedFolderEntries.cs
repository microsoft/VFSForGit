using System;
using System.Collections.Generic;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// This class stores the list of FolderEntryData objects for a FolderData ChildEntries in sorted order.
        /// The entries can be either FolderData objects or FileData objects in the sortedEntries list.
        /// </summary>
        internal class SortedFolderEntries
        {
            private const int PoolAllocationSize = 1000;

            private static ObjectPool<FolderData> folderPool = new ObjectPool<FolderData>(PoolAllocationSize, () => new FolderData());
            private static ObjectPool<FileData> filePool = new ObjectPool<FileData>(PoolAllocationSize, () => new FileData());

            private List<FolderEntryData> sortedEntries;

            public SortedFolderEntries()
            {
                this.sortedEntries = new List<FolderEntryData>();
            }

            public int Count
            {
                get { return this.sortedEntries.Count; }
            }

            public FolderEntryData this[int index]
            {
                get
                {
                   return this.sortedEntries[index];
                }
            }

            public static void FreePool()
            {
                folderPool.FreeAll();
                filePool.FreeAll();
            }

            public static bool ShrinkPool()
            {
                bool didShrink = folderPool.Shrink();
                didShrink = filePool.Shrink() == true ? true : didShrink;
                return didShrink;
            }

            public static int PoolSize()
            {
                return folderPool.Size + filePool.Size;
            }

            public void Clear()
            {
                this.sortedEntries.Clear();
            }

            public FolderData AddFolder(LazyUTF8String name)
            {
                int insertionIndex = this.GetInsertionIndex(name);
                return this.InsertFolder(name, insertionIndex);
            }

            public FileData AddFile(LazyUTF8String name, byte[] shaBytes)
            {
                int insertionIndex = this.GetInsertionIndex(name);
                return this.InsertFile(name, shaBytes, insertionIndex);
            }

            public FolderData GetOrAddFolder(LazyUTF8String name)
            {
                int index = this.GetSortedEntriesIndexOfName(name);
                if (index >= 0)
                {
                    return (FolderData)this.sortedEntries[index];
                }

                return this.InsertFolder(name, ~index);
            }

            public bool TryGetValue(LazyUTF8String name, out FolderEntryData value)
            {
                int index = this.GetSortedEntriesIndexOfName(name);
                if (index >= 0)
                {
                    value = this.sortedEntries[index];
                    return true;
                }

                value = null;
                return false;
            }

            private int GetInsertionIndex(LazyUTF8String name)
            {
                int insertionIndex = 0;
                if (this.sortedEntries.Count != 0)
                {
                    insertionIndex = this.GetSortedEntriesIndexOfName(name);
                    if (insertionIndex >= 0)
                    {
                        throw new InvalidOperationException("All entries should be unique");
                    }

                    // When the name is not found the returned value is the bitwise complement of
                    // where the name should be inserted to keep the sortedEntries in sorted order
                    insertionIndex = ~insertionIndex;
                }

                return insertionIndex;
            }

            private FolderData InsertFolder(LazyUTF8String name, int insertionIndex)
            {
                FolderData data = folderPool.GetNew();
                data.ResetData(name);
                this.sortedEntries.Insert(insertionIndex, data);
                return data;
            }

            private FileData InsertFile(LazyUTF8String name, byte[] shaBytes, int insertionIndex)
            {
                FileData data = filePool.GetNew();
                data.ResetData(name, shaBytes);
                this.sortedEntries.Insert(insertionIndex, data);
                return data;
            }

            /// <summary>
            /// Get the index of the name in the sorted folder entries list
            /// </summary>
            /// <param name="name">The name to search for in the entries</param>
            /// <returns>
            /// The zero based index of the entry if found;
            /// otherwise, a negative number that is the bitwise complement of the index of the next element that is larger than item or,
            /// if there is no larger element, the bitwise complement of this.entries.ObjectsUsed.
            /// </returns>
            private int GetSortedEntriesIndexOfName(LazyUTF8String name)
            {
                if (this.sortedEntries.Count == 0)
                {
                    return -1;
                }

                // Insertions are almost always at the end, because the inputs are pre-sorted by git.
                // We only have to insert at a different spot where Windows and git disagree on the sort order.
                int compareResult = this.sortedEntries[this.sortedEntries.Count - 1].Name.CaseInsensitiveCompare(name);
                if (compareResult == 0)
                {
                    return this.sortedEntries.Count - 1;
                }
                else if (compareResult < 0)
                {
                    return ~this.sortedEntries.Count;
                }

                int left = 0;
                int right = this.sortedEntries.Count - 2;

                while (right - left > 2)
                {
                    int middle = left + ((right - left) / 2);
                    int comparison = this.sortedEntries[middle].Name.CaseInsensitiveCompare(name);

                    if (comparison == 0)
                    {
                        return middle;
                    }

                    if (comparison < 0)
                    {
                        left = middle + 1;
                    }
                    else
                    {
                        right = middle - 1;
                    }
                }

                for (int i = right; i >= left; i--)
                {
                    compareResult = this.sortedEntries[i].Name.CaseInsensitiveCompare(name);
                    if (compareResult == 0)
                    {
                        return i;
                    }
                    else if (compareResult < 0)
                    {
                        return ~(i + 1);
                    }
                }

                return ~left;
            }
        }
    }
}
