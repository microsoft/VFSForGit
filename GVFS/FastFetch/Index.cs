using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GVFS.Common;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;

namespace FastFetch
{
    public class Index
    {
        // This versioning number lets us track compatibility with previous
        // versions of FastFetch regarding the index.  This should be bumped
        // when the index older versions of fastfetch created may not be compatible
        private const int CurrentFastFetchIndexVersion = 1;

        // Constants used for parsing an index entry
        private const ushort ExtendedBit = 0x4000;
        private const ushort SkipWorktreeBit = 0x4000;
        private const int BaseEntryLength = 62;

        // Buffer used to get path from index entry
        private const int MaxPathBufferSize = 4096;

        // Index default names
        private const string UpdatedIndexName = "index.updated";
        private const string BackupIndexName = "index.backup";

        // Location of the version marker file
        private readonly string versionMarkerFile;

        // Index paths
        private string indexPath;
        private string updatedIndexPath;
        private string backupIndexPath;

        private bool indexReadOnly;
        private ITracer tracer;
        private Dictionary<string, long> indexEntryOffsets;
        private string repoRoot;
        private MemoryMappedFile indexMapping;
        private MemoryMappedViewAccessor indexView;
        private uint indexVersion;
        private uint entryCount;

        /// <summary>
        /// Creates a new Index object to parse the current index
        /// Note that this constructor has a pretty specific use case.  When this constructor is used,
        /// the current index is about to be replaced (by the caller) with a new index.  So the current
        /// .git\index will be moved (by this constructor) to a backup location so it can be used to 
        /// populate the new index.
        /// </summary>
        /// <param name="repoRoot"></param>
        /// <param name="tracer"></param>
        public Index(
            string repoRoot,
            ITracer tracer)
            : this(repoRoot, tracer, indexReadOnly: false, indexFullPath: null, backupIndexFullPath: null, backupIndex: true)
        {
            this.MoveIndexToBackup();
        }

        private Index(
            string repoRoot,
            ITracer tracer,
            bool indexReadOnly,
            string indexFullPath,
            string backupIndexFullPath,
            bool backupIndex)
        {
            this.tracer = tracer;
            this.repoRoot = repoRoot;
            this.indexReadOnly = indexReadOnly;
            this.indexPath = indexFullPath ?? Path.Combine(repoRoot, GVFSConstants.DotGit.Index);
            this.updatedIndexPath = indexReadOnly ? this.indexPath : Path.Combine(repoRoot, GVFSConstants.DotGit.Root, UpdatedIndexName);
            if (backupIndex && !indexReadOnly)
            {
                this.backupIndexPath = backupIndexFullPath ?? Path.Combine(repoRoot, GVFSConstants.DotGit.Root, BackupIndexName);
            }

            this.versionMarkerFile = Path.Combine(this.repoRoot, GVFSConstants.DotGit.Root, ".fastfetch", "VersionMarker");
        }

        /// <summary>
        /// Updates entries in the current index with file sizes and times
        /// Algorithm:
        ///     1) If there was an index in place when this object was constructed, then:
        ///      a) Copy all valid entries (below) from the previous index to the new index
        ///      b) Conditionally (below) get times/sizes from the working tree for files not updated from the previous index
        ///     
        ///     2) If there was no index in place, conditionally populate all entries from disk
        ///     
        /// Conditions:
        /// - Working tree is only searched if allowUpdateFromWorkingTree is specified
        /// - A valid entry is an entry that exist and has a non-zero creation time (ctime)
        /// </summary>
        /// <param name="addedOrEditedLocalFiles">A collection of added or edited files</param>
        /// <param name="allowUpdateFromWorkingTree">Set to true if the working tree is known good and can be used during the update.</param>
        public void UpdateFileSizesAndTimes(BlockingCollection<string> addedOrEditedLocalFiles, bool allowUpdateFromWorkingTree)
        {
            using (ITracer activity = this.tracer.StartActivity("UpdateFileSizesAndTimes", EventLevel.Informational, Keywords.Telemetry, null))
            {
                this.CreateWorkingFiles();

                this.Parse();

                bool previousIndexFound = false;
                bool anyEntriesUpdated = false;

                if (this.IsFastFetchVersionMarkerCurrent())
                {
                    // Only populate from the previous index if we believe it's good to populate from
                    // For now, a current FastFetch version marker is the only criteria
                    anyEntriesUpdated |= this.UpdateFileInformationFromBackup(allowUpdateFromWorkingTree, out previousIndexFound);
                    if (previousIndexFound && (addedOrEditedLocalFiles != null))
                    {
                        // always update these files from disk or the index won't have good information
                        // for them and they'll show as modified even those not actually modified.
                        anyEntriesUpdated |= this.UpdateFileInformationFromDiskForFiles(addedOrEditedLocalFiles);
                    }
                }

                // If we didn't update from a previous index, update from the working tree if allowed.
                if (!previousIndexFound && allowUpdateFromWorkingTree)
                {
                    anyEntriesUpdated |= this.UpdateFileInformationFromWorkingTree();
                }

                if (anyEntriesUpdated)
                {
                    this.MoveUpdatedIndexToFinalLocation();
                }
            }
        }

        private void MoveIndexToBackup()
        {
            if (this.backupIndexPath != null)
            {
                if (File.Exists(this.indexPath))
                {
                    // Note that this moves the current index, leaving nothing behind
                    // This is intentional as we only need it for the purpose of updating the
                    // new index and leaving it behind can make updating slower.
                    this.tracer.RelatedEvent(EventLevel.Informational, "CreateBackup", new EventMetadata() { { "BackupIndexName", this.backupIndexPath } });
                    File.Delete(this.backupIndexPath);
                    File.Move(this.indexPath, this.backupIndexPath);
                }
                else
                {
                    this.tracer.RelatedEvent(EventLevel.Informational, "CreateBackup", new EventMetadata() { { "BackupIndexName", "none" } });
                    this.backupIndexPath = null;
                }
            }
        }

        private void CreateWorkingFiles()
        {
            if (!this.indexReadOnly)
            {
                File.Copy(this.indexPath, this.updatedIndexPath, overwrite: true);
            }
        }

        private bool UpdateFileInformationFromWorkingTree()
        {
            long updatedEntries = 0;

            using (ITracer activity = this.tracer.StartActivity("UpdateFileInformationFromWorkingTree", EventLevel.Informational, Keywords.Telemetry, null))
            {
                WorkingTree.ForAllFiles(
                    this.repoRoot,
                    (path, files) =>
                    {
                        foreach (FileInfo file in files)
                        {
                            string gitPath = file.FullName.FromWindowsFullPathToGitRelativePath(this.repoRoot);
                            long offset;
                            if (this.indexEntryOffsets.TryGetValue(gitPath, out offset))
                            {
                                IndexEntry indexEntry = new IndexEntry(this, offset);
                                indexEntry.Mtime = file.LastWriteTimeUtc;
                                indexEntry.Ctime = file.CreationTimeUtc;
                                indexEntry.Size = (uint)file.Length;
                                Interlocked.Increment(ref updatedEntries);
                            }
                        }
                    });

                this.indexView.Flush();
            }

            return updatedEntries > 0;
        }

        private bool UpdateFileInformationFromDiskForFiles(BlockingCollection<string> addedOrEditedLocalFiles)
        {
            long updatedEntriesFromDisk = 0;
            using (ITracer activity = this.tracer.StartActivity("UpdateDownloadedFiles", EventLevel.Informational, Keywords.Telemetry, null))
            {
                Parallel.ForEach(
                addedOrEditedLocalFiles,
                (localPath) =>
                {
                    string gitPath = localPath.FromWindowsFullPathToGitRelativePath(this.repoRoot);
                    long offset;
                    if (this.indexEntryOffsets.TryGetValue(gitPath, out offset))
                    {
                        UpdateEntryFromDisk(this, localPath, offset, ref updatedEntriesFromDisk);
                    }
                });
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "UpdateFileInformationFromDiskForFiles", new EventMetadata() { { "UpdatedFromDisk", updatedEntriesFromDisk } }, Keywords.Telemetry);
            return updatedEntriesFromDisk > 0;
        }

        private bool UpdateFileInformationFromBackup(bool shouldAlsoTryPopulateFromDisk, out bool indexFound)
        {
            indexFound = (this.backupIndexPath != null) && File.Exists(this.backupIndexPath);
            if (!indexFound)
            {
                return false;
            }

            using (ITracer activity = this.tracer.StartActivity("UpdateFileInformationFromPreviousIndex", EventLevel.Informational, Keywords.Telemetry, null))
            {
                Index backupIndex = new Index(this.repoRoot, this.tracer, indexReadOnly: true, indexFullPath: this.backupIndexPath, backupIndexFullPath: null, backupIndex: false);
                backupIndex.Parse();
                return this.UpdateFileInformationFromAnotherIndex(backupIndex, shouldAlsoTryPopulateFromDisk);
            }
        }

        private bool UpdateFileInformationFromAnotherIndex(Index otherIndex, bool shouldAlsoTryPopulateFromDisk)
        {
            long updatedEntriesFromOtherIndex = 0;
            foreach (KeyValuePair<string, long> i in this.indexEntryOffsets)
            {
                string currentIndexFilename = i.Key;
                long currentIndexOffset = i.Value;
                if (IndexEntry.HasUninitializedCTimeEntry(this, currentIndexOffset))
                {
                    long otherIndexOffset;
                    if (otherIndex.indexEntryOffsets.TryGetValue(currentIndexFilename, out otherIndexOffset))
                    {
                        if (!IndexEntry.HasUninitializedCTimeEntry(otherIndex, otherIndexOffset))
                        {
                            IndexEntry currentIndexEntry = new IndexEntry(this, currentIndexOffset);
                            IndexEntry otherIndexEntry = new IndexEntry(otherIndex, otherIndexOffset);
                            currentIndexEntry.CtimeSeconds = otherIndexEntry.CtimeSeconds;
                            currentIndexEntry.CtimeNanosecondFraction = otherIndexEntry.CtimeNanosecondFraction;
                            currentIndexEntry.MtimeSeconds = otherIndexEntry.MtimeSeconds;
                            currentIndexEntry.MtimeNanosecondFraction = otherIndexEntry.MtimeNanosecondFraction;
                            currentIndexEntry.Size = otherIndexEntry.Size;
                            ++updatedEntriesFromOtherIndex;
                        }
                    }
                }
            }

            long updatedEntriesFromDisk = 0;
            if (shouldAlsoTryPopulateFromDisk)
            {
                Parallel.ForEach(
                this.indexEntryOffsets.Where(entry => IndexEntry.HasUninitializedCTimeEntry(this, entry.Value)),
                (entry) =>
                {
                    string localPath = entry.Key.FromGitRelativePathToWindowsFullPath(this.repoRoot);
                    UpdateEntryFromDisk(this, localPath, entry.Value, ref updatedEntriesFromDisk);
                });

                this.tracer.RelatedEvent(EventLevel.Informational, "UpdateFileInformationFromAnotherIndex", new EventMetadata() { { "UpdatedFromOtherIndex", updatedEntriesFromOtherIndex }, { "UpdatedFromDisk", updatedEntriesFromDisk } }, Keywords.Telemetry);
            }

            this.indexView.Flush();

            return (updatedEntriesFromOtherIndex > 0) || (updatedEntriesFromDisk > 0);
        }

        private void UpdateEntryFromDisk(Index index, string localPath, long offset, ref long counter)
        {
            try
            {
                FileInfo file = new FileInfo(localPath);
                if (file.Exists)
                {
                    IndexEntry indexEntry = new IndexEntry(index, offset);
                    indexEntry.Mtime = file.LastWriteTimeUtc;
                    indexEntry.Ctime = file.CreationTimeUtc;
                    indexEntry.Size = (uint)file.Length;
                    Interlocked.Increment(ref counter);
                }
            }
            catch (System.Security.SecurityException)
            {
                // Skip these.
            }
            catch (System.UnauthorizedAccessException)
            {
                // Skip these.
            }
        }

        private void MoveUpdatedIndexToFinalLocation()
        {
            if (this.indexView != null)
            {
                this.indexView.Flush();
                this.indexView.Dispose();
                this.indexView = null;
            }

            if (this.indexMapping != null)
            {
                this.indexMapping.Dispose();
                this.indexView = null;
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "MoveUpdatedIndexToFinalLocation", new EventMetadata() { { "UpdatedIndex", this.updatedIndexPath }, { "Index", this.indexPath } });
            File.Delete(this.indexPath);
            File.Move(this.updatedIndexPath, this.indexPath);

            this.WriteFastFetchIndexVersionMarker();
        }

        private void WriteFastFetchIndexVersionMarker()
        {
            if (File.Exists(this.versionMarkerFile))
            {
                File.SetAttributes(this.versionMarkerFile, FileAttributes.Normal);
            }

            File.WriteAllText(this.versionMarkerFile, CurrentFastFetchIndexVersion.ToString(), Encoding.ASCII);
            File.SetAttributes(this.versionMarkerFile, FileAttributes.ReadOnly);
            this.tracer.RelatedEvent(EventLevel.Informational, "MarkerWritten", new EventMetadata() { { "Version", CurrentFastFetchIndexVersion } });
        }

        private bool IsFastFetchVersionMarkerCurrent() 
        {
            if (File.Exists(this.versionMarkerFile))
            {
                int version;
                string marker = File.ReadAllText(this.versionMarkerFile, Encoding.ASCII);
                bool isMarkerCurrent = int.TryParse(marker, out version) && (version == CurrentFastFetchIndexVersion);
                this.tracer.RelatedEvent(EventLevel.Informational, "PreviousMarker", new EventMetadata() { { "Content", marker }, { "IsCurrent", isMarkerCurrent } }, Keywords.Telemetry);
                return isMarkerCurrent;
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "NoPreviousMarkerFound", null, Keywords.Telemetry);
            return false;
        }

        private void Parse()
        {
            using (ITracer activity = this.tracer.StartActivity("ParseIndex", EventLevel.Informational, Keywords.Telemetry, new EventMetadata() { { "Index", this.updatedIndexPath } }))
            {
                this.indexMapping = MemoryMappedFile.CreateFromFile(this.updatedIndexPath, FileMode.Open);
                this.indexView = this.indexMapping.CreateViewAccessor();
                using (MemoryMappedViewStream indexStream = this.indexMapping.CreateViewStream())
                {
                    this.ParseIndex(indexStream, updateOffsetsOnly: false);
                }
            }
        }

        private void ParseIndex(MemoryMappedViewStream indexStream, bool updateOffsetsOnly = false)
        {
            byte[] buffer = new byte[40];
            indexStream.Position = 0;

            byte[] signature = new byte[4];
            indexStream.Read(signature, 0, 4);
            this.indexVersion = this.ReadUInt32(buffer, indexStream);
            this.entryCount = this.ReadUInt32(buffer, indexStream);

            this.tracer.RelatedEvent(EventLevel.Informational, "IndexData", new EventMetadata() { { "Index", this.updatedIndexPath }, { "Version", this.indexVersion }, { "entryCount", this.entryCount } }, Keywords.Telemetry);

            this.indexEntryOffsets = new Dictionary<string, long>((int)this.entryCount, StringComparer.OrdinalIgnoreCase);

            int previousPathLength = 0;
            byte[] pathBuffer = new byte[MaxPathBufferSize];
            for (int i = 0; i < this.entryCount; i++)
            {
                // See https://github.com/git/git/blob/867b1c1bf68363bcfd17667d6d4b9031fa6a1300/Documentation/technical/index-format.txt#L38
                long entryOffset = indexStream.Position;

                int entryLength = BaseEntryLength;

                // Skip the next 60 bytes.
                // 40 bytes encapsulated by IndexEntry but not needed now.
                // 20 bytes of sha
                indexStream.Position += 60;

                ushort flags = this.ReadUInt16(buffer, indexStream);
                bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                int pathLength = (ushort)(((flags << 20) >> 20) & 4095);
                entryLength += pathLength;

                bool skipWorktree = false;
                if (isExtended && (this.indexVersion > 2))
                {
                    ushort extendedFlags = this.ReadUInt16(buffer, indexStream);
                    skipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                    entryLength += 2;
                }

                if (this.indexVersion == 4)
                {
                    int replaceLength = this.ReadReplaceLength(indexStream);
                    int replaceIndex = previousPathLength - replaceLength;
                    indexStream.Read(pathBuffer, replaceIndex, pathLength - replaceIndex + 1);
                    previousPathLength = pathLength;
                }
                else
                {
                    // Simple paths but 1 - 8 nul bytes as necessary to pad the entry to a multiple of eight bytes
                    int numNulBytes = 8 - (entryLength % 8);
                    indexStream.Read(pathBuffer, 0, pathLength + numNulBytes);
                }

                if (!skipWorktree)  
                {
                    // Examine only the things we're not skipping...
                    // Potential Future Perf Optimization: Perform this work on multiple threads.  If we take the first byte and % by number of threads,
                    // we can ensure that all entries for a given folder end up in the same dictionary              
                    string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                    this.indexEntryOffsets[path] = entryOffset;
                }
            }
        }

        /// <summary>
        /// Get the length of the replacement string.  For definition of data, see:
        /// https://github.com/git/git/blob/867b1c1bf68363bcfd17667d6d4b9031fa6a1300/Documentation/technical/index-format.txt#L38
        /// </summary>
        /// <param name="stream">stream to read bytes from</param>
        /// <returns></returns>
        private int ReadReplaceLength(Stream stream)
        {
            int headerByte = stream.ReadByte();
            int offset = headerByte & 0x7f;

            // Terminate the loop when the high bit is no longer set.
            for (int i = 0; (headerByte & 0x80) != 0; i++)
            {
                headerByte = stream.ReadByte();

                offset += 1;
                offset = (offset << 7) + (headerByte & 0x7f);
            }

            return offset;
        }

        private uint ReadUInt32(byte[] buffer, Stream stream)
        {
            buffer[3] = (byte)stream.ReadByte();
            buffer[2] = (byte)stream.ReadByte();
            buffer[1] = (byte)stream.ReadByte();
            buffer[0] = (byte)stream.ReadByte();

            return BitConverter.ToUInt32(buffer, 0);
        }

        private ushort ReadUInt16(byte[] buffer, Stream stream)
        {
            buffer[1] = (byte)stream.ReadByte();
            buffer[0] = (byte)stream.ReadByte();

            // (ushort)BitConverter.ToInt16 avoids the running the duplicated checks in ToUInt16
            return (ushort)BitConverter.ToInt16(buffer, 0);
        }

        /// <summary>
        /// Private helper class to read/write specific values from a Git Index entry based on offset in a view.
        /// </summary>
        private class IndexEntry
        {
            private const long UnixEpochMilliseconds = 116444736000000000;
            private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            private Index index;

            public IndexEntry(Index index, long offset)
            {
                this.index = index;
                this.Offset = offset;
            }

            // EntryOffsets is the offset from the start of a index entry where specific data exists
            // For more information about the layout of git index entries, see:
            // https://github.com/git/git/blob/867b1c1bf68363bcfd17667d6d4b9031fa6a1300/Documentation/technical/index-format.txt#L38
            private enum EntryOffsets
            {
                ctimeSeconds = 0,
                ctimeNanoseconds = 4,
                mtimeSeconds = 8,
                mtimeNanoseconds = 12,
                filesize = 36,
                flags = 80,
                extendedFlags = 82,
            }

            public long Offset { get; set; }

            public uint CtimeSeconds
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.ctimeSeconds);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.ctimeSeconds, value);
                }
            }

            public uint CtimeNanosecondFraction
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.ctimeNanoseconds);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.ctimeNanoseconds, value);
                }
            }

            public DateTime Ctime
            {
                get
                {
                    return this.ToWindowsTime(this.CtimeSeconds, this.CtimeNanosecondFraction);
                }

                set
                {
                    IndexEntryTime time = this.ToUnixTime(value);
                    this.CtimeSeconds = time.Seconds;
                    this.CtimeNanosecondFraction = time.NanosecondFraction;
                }
            }

            public uint MtimeSeconds
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.mtimeSeconds);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.mtimeSeconds, value);
                }
            }

            public uint MtimeNanosecondFraction
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.mtimeNanoseconds);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.mtimeNanoseconds, value);
                }
            }

            public DateTime Mtime
            {
                get
                {
                    return this.ToWindowsTime(this.MtimeSeconds, this.MtimeNanosecondFraction);
                }

                set
                {
                    IndexEntryTime times = this.ToUnixTime(value);
                    this.MtimeSeconds = times.Seconds;
                    this.MtimeNanosecondFraction = times.NanosecondFraction;
                }
            }

            public uint Size
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.filesize);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.filesize, value);
                }
            }

            public ushort Flags
            {
                get
                {
                    return this.ReadUInt16(EntryOffsets.flags);
                }

                set
                {
                    this.WriteUInt16(EntryOffsets.flags, value);
                }
            }

            public bool IsExtended
            {
                get
                {
                    return (this.Flags & Index.ExtendedBit) == Index.ExtendedBit;
                }
            }

            public ushort ExtendedFlags
            {
                get
                {
                    return (this.IsExtended && (this.index.indexVersion > 2)) ? this.ReadUInt16(EntryOffsets.extendedFlags) : (ushort)0;
                }

                set
                {
                    if (this.IsExtended)
                    {
                        this.WriteUInt32(EntryOffsets.extendedFlags, value);
                    }
                }
            }

            public static bool HasUninitializedCTimeEntry(Index index, long offset)
            {
                return EndianHelper.Swap(index.indexView.ReadUInt32(offset + (long)EntryOffsets.ctimeSeconds)) == 0;
            }

            private uint ReadUInt32(EntryOffsets fromOffset)
            {
                return EndianHelper.Swap(this.index.indexView.ReadUInt32(this.Offset + (long)fromOffset));
            }

            private void WriteUInt32(EntryOffsets fromOffset, uint data)
            {
                this.index.indexView.Write(this.Offset + (long)fromOffset, EndianHelper.Swap(data));
            }

            private ushort ReadUInt16(EntryOffsets fromOffset)
            {
                return EndianHelper.Swap(this.index.indexView.ReadUInt16(this.Offset + (long)fromOffset));
            }

            private void WriteUInt16(EntryOffsets fromOffset, ushort data)
            {
                this.index.indexView.Write(this.Offset + (long)fromOffset, EndianHelper.Swap(data));
            }

            private IndexEntryTime ToUnixTime(DateTime datetime)
            {
                if (datetime > UnixEpoch)
                {
                    // Using the same FileTime -> Unix time conversion that Git uses.
                    long unixEpochRelativeNanoseconds = datetime.ToFileTime() - IndexEntry.UnixEpochMilliseconds;
                    uint wholeSeconds = (uint)(unixEpochRelativeNanoseconds / (long)10000000);
                    uint nanosecondFraction = (uint)((unixEpochRelativeNanoseconds % 10000000) * 100);

                    return new IndexEntryTime() { Seconds = wholeSeconds, NanosecondFraction = nanosecondFraction };
                }
                else
                {
                    return new IndexEntryTime() { Seconds = 0, NanosecondFraction = 0 };
                }
            }

            private DateTime ToWindowsTime(uint seconds, uint nanosecondFraction)
            {
                DateTime time = UnixEpoch.AddSeconds(seconds).AddMilliseconds(nanosecondFraction * 1000000);
                return time;
            }

            private class IndexEntryTime
            {
                public uint Seconds { get; set; }
                public uint NanosecondFraction { get; set; }
            }
        }
    }
}
