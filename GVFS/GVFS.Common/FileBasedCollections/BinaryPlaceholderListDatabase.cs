using GVFS.Common.FileBasedCollections;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.FileBasedCollections
{
    public class BinaryPlaceholderListDatabase : BinaryFileBasedCollection<PlaceholderEvent>
    {
        // Special folder values must:
        // - Be 40 characters long
        // - Not be a valid SHA-1 value (to avoid collisions with files)

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
        private List<Tuple<byte, PlaceholderEvent>> placeholderChangesWhileRebuildingList;

        private BinaryPlaceholderListDatabase(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath)
            : base(tracer, fileSystem, dataFilePath, true, BinaryPlaceholderListDatabase.SerializeEvent)
        {
        }

        /// <summary>
        /// The EstimatedCount is "estimated" because it's simply (# adds - # deletes).  There is nothing to prevent
        /// multiple adds or deletes of the same path from being double counted
        /// </summary>
        public int EstimatedCount { get; private set; }

        public static bool TryCreate(ITracer tracer, string dataFilePath, PhysicalFileSystem fileSystem, out BinaryPlaceholderListDatabase output, out string error)
        {
            BinaryPlaceholderListDatabase temp = new BinaryPlaceholderListDatabase(tracer, fileSystem, dataFilePath);

            // We don't want to cache placeholders so this just serves to validate early and populate count.
            if (!temp.TryLoadFromDisk<string, PlaceholderEvent>(
                temp.TryParseAddLine,
                temp.TryParseRemoveLine,
                (key, value) => temp.EstimatedCount++,
                (key) => temp.EstimatedCount--,
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
            this.AddAndFlush(new AddFileEntry(path, sha));
        }

        public void AddAndFlushFolder(string path, bool isExpanded)
        {
            this.AddAndFlush(new AddFolderEntry(path, isExpanded));
        }

        public void RemoveAndFlush(string path)
        {
            try
            {
                PlaceholderEvent data = new PlaceholderRemoved(path);
                this.WriteRemoveEntry(
                    data,
                    () =>
                    {
                        this.EstimatedCount--;
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            this.placeholderChangesWhileRebuildingList.Add(Tuple.Create(RemoveEntryPrefix, data));
                        }
                    });
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        /// <summary>
        /// Gets all entries and prepares the BinaryPlaceholderListDatabase for a call to WriteAllEntriesAndFlush.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// GetAllEntriesAndPrepToWriteAllEntries was called (a second time) without first calling WriteAllEntriesAndFlush.
        /// </exception>
        /// <remarks>
        /// Usage notes:
        ///     - All calls to GetAllEntriesAndPrepToWriteAllEntries must be paired with a subsequent call to WriteAllEntriesAndFlush
        ///     - If WriteAllEntriesAndFlush is *not* called entries that were added to the BinaryPlaceholderListDatabase after
        ///       calling GetAllEntriesAndPrepToWriteAllEntries will be lost
        /// </remarks>
        public List<PlaceholderEvent> GetAllEntriesAndPrepToWriteAllEntries()
        {
            try
            {
                Dictionary<string, PlaceholderEvent> placeholders = new Dictionary<string, PlaceholderEvent>(Math.Max(1, this.EstimatedCount));

                string error;
                if (!this.TryLoadFromDisk<string, PlaceholderEvent>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) => placeholders.TryAdd(key, value),
                    (key) => placeholders.Remove(key),
                    out error,
                    () =>
                    {
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            throw new InvalidOperationException($"BinaryPlaceholderListDatabase should always flush queue placeholders using WriteAllEntriesAndFlush before calling {nameof(this.GetAllEntriesAndPrepToWriteAllEntries)} again.");
                        }

                        this.placeholderChangesWhileRebuildingList = new List<Tuple<byte, PlaceholderEvent>>();
                    }))
                {
                    throw new InvalidDataException(error);
                }

                return placeholders.Values.ToList();
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        /// <summary>
        /// Gets all entries and prepares the BinaryPlaceholderListDatabase for a call to WriteAllEntriesAndFlush.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// GetAllEntriesAndPrepToWriteAllEntries was called (a second time) without first calling WriteAllEntriesAndFlush.
        /// </exception>
        /// <remarks>
        /// Usage notes:
        ///     - All calls to GetAllEntriesAndPrepToWriteAllEntries must be paired with a subsequent call to WriteAllEntriesAndFlush
        ///     - If WriteAllEntriesAndFlush is *not* called entries that were added to the BinaryPlaceholderListDatabase after
        ///       calling GetAllEntriesAndPrepToWriteAllEntries will be lost
        /// </remarks>
        public void GetAllEntriesAndPrepToWriteAllEntries(out IReadOnlyList<AddFileEntry> filePlaceholders, out IReadOnlyList<AddFolderEntry> folderPlaceholders)
        {
            try
            {
                IDictionary<string, AddFileEntry> filePlaceholdersFromDisk = new Dictionary<string, AddFileEntry>(Math.Max(1, this.EstimatedCount));
                IDictionary<string, AddFolderEntry> folderPlaceholdersFromDisk = new Dictionary<string, AddFolderEntry>(Math.Max(1, (int)(this.EstimatedCount * .3)));

                if (!this.TryLoadFromDisk<string, PlaceholderEvent>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) =>
                    {
                        switch (value)
                        {
                            case AddFileEntry addFileEntry:
                                filePlaceholdersFromDisk.TryAdd(key, addFileEntry);
                                break;
                            case AddFolderEntry addFolderEntry:
                                folderPlaceholdersFromDisk.TryAdd(key, addFolderEntry);
                                break;
                            default:
                                throw new ArgumentException($"Parsed value not of a supported type");
                        }
                    },
                    (key) =>
                    {
                        if (!filePlaceholdersFromDisk.Remove(key))
                        {
                            folderPlaceholdersFromDisk.Remove(key);
                        }
                    },
                    out string error,
                    () =>
                    {
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            throw new InvalidOperationException($"BinaryPlaceholderListDatabase should always flush queue placeholders using WriteAllEntriesAndFlush before calling {(nameof(this.GetAllEntriesAndPrepToWriteAllEntries))} again.");
                        }

                        this.placeholderChangesWhileRebuildingList = new List<Tuple<byte, PlaceholderEvent>>();
                    }))
                {
                    throw new InvalidDataException(error);
                }

                filePlaceholders = filePlaceholdersFromDisk.Values.ToList();
                folderPlaceholders = folderPlaceholdersFromDisk.Values.ToList();
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public Dictionary<string, AddFileEntry> GetAllFileEntries()
        {
            try
            {
                Dictionary<string, AddFileEntry> filePlaceholdersFromDiskByPath =
                    new Dictionary<string, AddFileEntry>(Math.Max(1, this.EstimatedCount), StringComparer.Ordinal);

                string error;
                if (!this.TryLoadFromDisk<string, PlaceholderEvent>(
                    this.TryParseAddLine,
                    this.TryParseRemoveLine,
                    (key, value) =>
                    {
                        if (value is AddFileEntry)
                        {
                            filePlaceholdersFromDiskByPath[key] = (AddFileEntry)value;
                        }
                    },
                    key => filePlaceholdersFromDiskByPath.Remove(key),
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

        public void WriteAllEntriesAndFlush(IEnumerable<PlaceholderEvent> updatedPlaceholders)
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

        private static void SerializeEvent(BinaryWriter writer, PlaceholderEvent placeholderEvent)
        {
            placeholderEvent.Serialize(writer);
        }

        private IEnumerable<Tuple<byte, PlaceholderEvent>> GenerateDataLines(IEnumerable<PlaceholderEvent> updatedPlaceholders)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            this.EstimatedCount = 0;
            foreach (PlaceholderEvent updated in updatedPlaceholders)
            {
                if (keys.Add(updated.Path))
                {
                    this.EstimatedCount++;
                }

                yield return Tuple.Create(AddEntryPrefix, updated);
            }

            if (this.placeholderChangesWhileRebuildingList != null)
            {
                foreach (Tuple<byte, PlaceholderEvent> entry in this.placeholderChangesWhileRebuildingList)
                {
                    if (entry.Item1 == RemoveEntryPrefix)
                    {
                        if (keys.Remove(entry.Item2.Path))
                        {
                            this.EstimatedCount--;
                            yield return entry;
                        }
                    }
                    else
                    {
                        if (keys.Add(entry.Item2.Path))
                        {
                            this.EstimatedCount++;
                        }

                        yield return entry;
                    }
                }

                this.placeholderChangesWhileRebuildingList = null;
            }
        }

        private void AddAndFlush(PlaceholderEvent entry)
        {
            try
            {
                this.WriteAddEntry(
                    entry,
                    () =>
                    {
                        this.EstimatedCount++;
                        if (this.placeholderChangesWhileRebuildingList != null)
                        {
                            this.placeholderChangesWhileRebuildingList.Add(Tuple.Create(AddEntryPrefix, entry));
                        }
                    });
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private bool TryParseAddLine(BinaryReader reader, out string key, out PlaceholderEvent value, out string error)
        {
            // Expected: <FileOrFolderType><Path>[<sha>]
            byte type = reader.ReadByte();
            switch (type)
            {
                case PlaceholderEvent.FilePrefix:
                    value = new AddFileEntry(reader.ReadString(), new string(reader.ReadChars(40)));
                    break;
                case PlaceholderEvent.ExpandedFolderPrefix:
                case PlaceholderEvent.PartialFolderPrefix:
                    value = new AddFolderEntry(reader.ReadString(), type == PlaceholderEvent.ExpandedFolderPrefix);
                    break;
                default:
                    key = null;
                    value = null;
                    error = $"Invalid entry type {type}";
                    return false;
            }

            key = value.Path;
            error = null;
            return true;
        }

        private bool TryParseRemoveLine(BinaryReader reader, out string key, out string error)
        {
            // The key is a path taking the entire line.
            key = reader.ReadString();
            error = null;
            return true;
        }
    }
}
