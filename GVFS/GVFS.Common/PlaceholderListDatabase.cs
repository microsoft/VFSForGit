using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class PlaceholderListDatabase : FileBasedCollection
    {
        // Special folder values must:
        // - Be 40 characters long
        // - Not be a valid SHA-1 value (to avoid collisions with files)
        public const string PartialFolderValue = "                          PARTIAL FOLDER";
        public const string ExpandedFolderValue = "                         EXPANDED FOLDER";

        private const char PathTerminator = '\0';

        // This list holds placeholder entries that are created between calls to 
        // GetAllEntriesAndPrepToWriteAllEntries and WriteAllEntriesAndFlush.
        //
        //    Example:
        //
        //       1) VFS4G parses the updated index (as part of a projection change)
        //       2) VFS4G starts the work to update placeholders
        //       3) VFS4G calls GetAllEntriesAndPrepToWriteAllEntries
        //       4) VFS4G starts updating placeholders
        //       5) Some application reads a pure-virtual file (creating a new placeholder) while VFS4G is updating existing placeholders. 
        //          That new placeholder is added to placeholderChangesWhileRebuildingList.
        //       6) VFS4G completes updating the placeholders and calls WriteAllEntriesAndFlush. 
        //          Note: this list does *not* include the placeholders created in step 5, as the were not included in GetAllEntries.
        //       7) WriteAllEntriesAndFlush writes *both* the entires in placeholderDataEntries and those that were passed in as the parameter.
        //
        // This scenario is covered in the unit test PlaceholderDatabaseTests.HandlesRaceBetweenAddAndWriteAllEntries
        //
        // Because of this list, callers must always call WriteAllEntries after calling GetAllEntriesAndPrepToWriteAllEntries.
        // 
        // This list must always be accessed from inside one of FileBasedCollection's synchronizedAction callbacks because
        // there is race potential between creating the queue, adding to the queue, and writing to the data file.
        private List<PlaceholderDataEntry> placeholderChangesWhileRebuildingList;
        
        private PlaceholderListDatabase(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath)
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile: true)
        {
        }

        /// <summary>
        /// The EstimatedCount is "estimated" because it's simply (# adds - # deletes).  There is nothing to prevent
        /// multiple adds or deletes of the same path from being double counted
        /// </summary>
        public int EstimatedCount { get; private set; }

        public static bool TryCreate(ITracer tracer, string dataFilePath, PhysicalFileSystem fileSystem, out PlaceholderListDatabase output, out string error)
        {
            PlaceholderListDatabase temp = new PlaceholderListDatabase(tracer, fileSystem, dataFilePath);

            // We don't want to cache placeholders so this just serves to validate early and populate count.
            if (!temp.TryLoadFromDisk<string, string>(
                temp.TryParseAddLine,
                temp.TryParseRemoveLine,
                (key, value) => temp.EstimatedCount++,
                out error))
            {
                temp = null;
                output = null;
                return false;
            }

            error = null;
            output = temp;
            return true;
        }

        public void AddAndFlushFile(string path, string sha)
        {
            this.AddAndFlush(path, sha);
        }

        public void AddAndFlushFolder(string path, bool isExpanded)
        {
            this.AddAndFlush(path, isExpanded ? ExpandedFolderValue : PartialFolderValue);
        }

        public void RemoveAndFlush(string path)
        {
            try
            {
                this.WriteRemoveEntry(
                    path,
                    () =>
                    {
                        this.EstimatedCount--;
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            this.placeholderChangesWhileRebuildingList.Add(new PlaceholderDataEntry(path));
                        }
                    });
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        /// <summary>
        /// Gets all entries and intializes placeholderChangesWhileRebuildingList in preparation fr a call to WriteAllEntriesAndFlush.
        /// </summary>
        /// <remarks>
        /// See placeholderChangesWhileRebuildingList declaration for additional details as to why WriteAllEntriesAndFlush
        /// must be called.
        /// </remarks>
        public List<PlaceholderData> GetAllEntriesAndPrepToWriteAllEntries()
        {
            try
            {
                List<PlaceholderData> placeholders = new List<PlaceholderData>(Math.Max(1, this.EstimatedCount));

                string error;
                if (!this.TryLoadFromDisk<string, string>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) => placeholders.Add(new PlaceholderData(path: key, fileShaOrFolderValue: value)),
                    out error,
                    () =>
                    {
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                        throw new InvalidOperationException($"PlaceholderListDatabase should always flush queue placeholders using WriteAllEntriesAndFlush before calling {nameof(this.GetAllEntriesAndPrepToWriteAllEntries)} again.");
                        }

                        this.placeholderChangesWhileRebuildingList = new List<PlaceholderDataEntry>();
                    }))
                {
                    throw new InvalidDataException(error);
                }

                return placeholders;
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        /// <summary>
        /// Gets all entries and intializes placeholderChangesWhileRebuildingList in preparation fr a call to WriteAllEntriesAndFlush.
        /// </summary>
        /// <remarks>
        /// See placeholderChangesWhileRebuildingList declaration for additional details as to why WriteAllEntriesAndFlush
        /// must be called.
        /// </remarks>
        public void GetAllEntriesAndPrepToWriteAllEntries(out List<PlaceholderData> filePlaceholders, out List<PlaceholderData> folderPlaceholders)
        {
            try
            {
                List<PlaceholderData> filePlaceholdersFromDisk = new List<PlaceholderData>(Math.Max(1, this.EstimatedCount));
                List<PlaceholderData> folderPlaceholdersFromDisk = new List<PlaceholderData>(Math.Max(1, (int)(this.EstimatedCount * .3)));

                string error;
                if (!this.TryLoadFromDisk<string, string>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) =>
                    {
                        if (value == PartialFolderValue || value == ExpandedFolderValue)
                        {
                            folderPlaceholdersFromDisk.Add(new PlaceholderData(path: key, fileShaOrFolderValue: value));
                        }
                        else
                        {
                            filePlaceholdersFromDisk.Add(new PlaceholderData(path: key, fileShaOrFolderValue: value));
                        }
                    },
                    out error,
                    () =>
                    {
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            throw new InvalidOperationException($"PlaceholderListDatabase should always flush queue placeholders using WriteAllEntriesAndFlush before calling {(nameof(this.GetAllEntriesAndPrepToWriteAllEntries))} again.");
                        }

                        this.placeholderChangesWhileRebuildingList = new List<PlaceholderDataEntry>();
                    }))
                {
                    throw new InvalidDataException(error);
                }

                filePlaceholders = filePlaceholdersFromDisk;
                folderPlaceholders = folderPlaceholdersFromDisk;
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public Dictionary<string, PlaceholderListDatabase.PlaceholderData> GetAllFileEntries()
        {
            try
            {
                Dictionary<string, PlaceholderListDatabase.PlaceholderData> filePlaceholdersFromDiskByPath = 
                    new Dictionary<string, PlaceholderListDatabase.PlaceholderData>(Math.Max(1, this.EstimatedCount), StringComparer.Ordinal);

                string error;
                if (!this.TryLoadFromDisk<string, string>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) =>
                    {
                        if (value != PartialFolderValue && value != ExpandedFolderValue)
                        {
                            filePlaceholdersFromDiskByPath[key] = new PlaceholderData(path: key, fileShaOrFolderValue: value);
                        }
                    },
                    out error))
                {
                    throw new InvalidDataException(error);
                }

                return filePlaceholdersFromDiskByPath;
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public void WriteAllEntriesAndFlush(IEnumerable<PlaceholderData> updatedPlaceholders)
        {
            try
            {
                this.WriteAndReplaceDataFile(() => this.GenerateDataLines(updatedPlaceholders));
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private IEnumerable<string> GenerateDataLines(IEnumerable<PlaceholderData> updatedPlaceholders)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            this.EstimatedCount = 0;
            foreach (PlaceholderData updated in updatedPlaceholders)
            {
                if (keys.Add(updated.Path))
                {
                    this.EstimatedCount++;
                }

                yield return this.FormatAddLine(updated.Path + PathTerminator + updated.Sha);
            }

            if (this.placeholderChangesWhileRebuildingList != null)
            {
                foreach (PlaceholderDataEntry entry in this.placeholderChangesWhileRebuildingList)
                {
                    if (entry.DeleteEntry)
                    {
                        if (keys.Remove(entry.Path))
                        {
                            this.EstimatedCount--;
                            yield return this.FormatRemoveLine(entry.Path);
                        }
                    }
                    else
                    {
                        if (keys.Add(entry.Path))
                        {
                            this.EstimatedCount++;
                        }

                        yield return this.FormatAddLine(entry.Path + PathTerminator + entry.Sha);
                    }
                }

                this.placeholderChangesWhileRebuildingList = null;
            }
        }

        private void AddAndFlush(string path, string sha)
        {
            try
            {
                this.WriteAddEntry(
                    path + PathTerminator + sha,
                    () =>
                    {
                        this.EstimatedCount++;
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            this.placeholderChangesWhileRebuildingList.Add(new PlaceholderDataEntry(path, sha));
                        }
                    });
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private bool TryParseAddLine(string line, out string key, out string value, out string error)
        {
            // Expected: <Placeholder-Path>\0<40-Char-SHA1>
            int idx = line.IndexOf(PathTerminator);
            if (idx < 0)
            {
                key = null;
                value = null;
                error = "Add line missing path terminator: " + line;
                return false;
            }

            if (idx + 1 + GVFSConstants.ShaStringLength != line.Length)
            {
                key = null;
                value = null;
                error = $"Invalid SHA1 length {line.Length - idx - 1}: " + line;
                return false;
            }

            key = line.Substring(0, idx);
            value = line.Substring(idx + 1, GVFSConstants.ShaStringLength);

            error = null;
            return true;
        }

        private bool TryParseRemoveLine(string line, out string key, out string error)
        {
            // The key is a path taking the entire line.
            key = line;
            error = null;
            return true;
        }

        public class PlaceholderData
        {
            public PlaceholderData(string path, string fileShaOrFolderValue)
            {
                this.Path = path;
                this.Sha = fileShaOrFolderValue;
            }

            public string Path { get; }
            public string Sha { get; }

            public bool IsFolder
            {
                get 
                {
                    return this.Sha == PartialFolderValue || this.IsExpandedFolder;
                }
            }

            public bool IsExpandedFolder
            {
                get
                {
                    return this.Sha == ExpandedFolderValue;
                }
            }
        }

        private class PlaceholderDataEntry
        {
            public PlaceholderDataEntry(string path, string sha)
            {
                this.Path = path;
                this.Sha = sha;
                this.DeleteEntry = false;
            }

            public PlaceholderDataEntry(string path)
            {
                this.Path = path;
                this.Sha = null;
                this.DeleteEntry = true;
            }

            public string Path { get; }
            public string Sha { get; }
            public bool DeleteEntry { get; }
        }
    }
}
