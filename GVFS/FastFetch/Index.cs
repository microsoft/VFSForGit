using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        private static readonly byte[] MagicSignature = new byte[] { (byte)'D', (byte)'I', (byte)'R', (byte)'C' };

        // Location of the version marker file
        private readonly string versionMarkerFile;

        private readonly bool readOnly;

        // Index paths
        private readonly string indexPath;
        private readonly string updatedIndexPath;

        private readonly ITracer tracer;
        private readonly string repoRoot;

        private Dictionary<string, long> indexEntryOffsets;
        private uint entryCount;

        /// <summary>
        /// Creates a new Index object to parse the specified index file
        /// </summary>
        public Index(
            string repoRoot,
            ITracer tracer,
            string indexFullPath,
            bool readOnly)
        {
            this.tracer = tracer;
            this.repoRoot = repoRoot;
            this.indexPath = indexFullPath;
            this.readOnly = readOnly;

            if (this.readOnly)
            {
                this.updatedIndexPath = this.indexPath;
            }
            else
            {
                this.updatedIndexPath = Path.Combine(repoRoot, GVFSConstants.DotGit.Root, UpdatedIndexName);
            }

            this.versionMarkerFile = Path.Combine(this.repoRoot, GVFSConstants.DotGit.Root, ".fastfetch", "VersionMarker");
        }

        public uint IndexVersion { get; private set; }

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
        /// <param name="backupIndex">An optional index to source entry values from</param>
        public void UpdateFileSizesAndTimes(BlockingCollection<string> addedOrEditedLocalFiles, bool allowUpdateFromWorkingTree, bool shouldSignIndex, Index backupIndex = null)
        {
            if (this.readOnly)
            {
                throw new InvalidOperationException("Cannot update a readonly index.");
            }

            using (ITracer activity = this.tracer.StartActivity("UpdateFileSizesAndTimes", EventLevel.Informational, Keywords.Telemetry, null))
            {
                File.Copy(this.indexPath, this.updatedIndexPath, overwrite: true);

                this.Parse();

                bool anyEntriesUpdated = false;

                using (MemoryMappedFile mmf = this.GetMemoryMappedFile())
                using (MemoryMappedViewAccessor indexView = mmf.CreateViewAccessor())
                {
                    // Only populate from the previous index if we believe it's good to populate from
                    // For now, a current FastFetch version marker is the only criteria
                    if (backupIndex != null)
                    {
                        if (this.IsFastFetchVersionMarkerCurrent())
                        {
                            using (this.tracer.StartActivity("UpdateFileInformationFromPreviousIndex", EventLevel.Informational, Keywords.Telemetry, null))
                            {
                                anyEntriesUpdated |= this.UpdateFileInformationForAllEntries(indexView, backupIndex, allowUpdateFromWorkingTree);
                            }

                            if (addedOrEditedLocalFiles != null)
                            {
                                // always update these files from disk or the index won't have good information
                                // for them and they'll show as modified even those not actually modified.
                                anyEntriesUpdated |= this.UpdateFileInformationFromDiskForFiles(indexView, addedOrEditedLocalFiles);
                            }
                        }
                    }
                    else if (allowUpdateFromWorkingTree)
                    {
                        // If we didn't update from a previous index, update from the working tree if allowed.
                        anyEntriesUpdated |= this.UpdateFileInformationFromWorkingTree(indexView);
                    }

                    indexView.Flush();
                }

                if (anyEntriesUpdated)
                {
                    this.MoveUpdatedIndexToFinalLocation(shouldSignIndex);
                }
                else
                {
                    File.Delete(this.updatedIndexPath);
                }
            }
        }

        public void Parse()
        {
            using (ITracer activity = this.tracer.StartActivity("ParseIndex", EventLevel.Informational, Keywords.Telemetry, new EventMetadata() { { "Index", this.updatedIndexPath } }))
            {
                using (Stream indexStream = new FileStream(this.updatedIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    this.ParseIndex(indexStream);
                }
            }
        }

        private static string FromDotnetFullPathToGitRelativePath(string path, string repoRoot)
        {
            return path.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
        }

        private static string FromGitRelativePathToDotnetFullPath(string path, string repoRoot)
        {
            return Path.Combine(repoRoot, path.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar));
        }

        private MemoryMappedFile GetMemoryMappedFile()
        {
            return MemoryMappedFile.CreateFromFile(this.updatedIndexPath, FileMode.Open);
        }

        private bool UpdateFileInformationFromWorkingTree(MemoryMappedViewAccessor indexView)
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
                            string gitPath = FromDotnetFullPathToGitRelativePath(file.FullName, this.repoRoot);
                            long offset;
                            if (this.indexEntryOffsets.TryGetValue(gitPath, out offset))
                            {
                                if (NativeMethods.TryStatFileAndUpdateIndex(this.tracer, gitPath, indexView, offset))
                                {
                                    Interlocked.Increment(ref updatedEntries);
                                }
                            }
                        }
                    });
            }

            return updatedEntries > 0;
        }

        private bool UpdateFileInformationFromDiskForFiles(MemoryMappedViewAccessor indexView, BlockingCollection<string> addedOrEditedLocalFiles)
        {
            long updatedEntriesFromDisk = 0;
            using (ITracer activity = this.tracer.StartActivity("UpdateDownloadedFiles", EventLevel.Informational, Keywords.Telemetry, null))
            {
                Parallel.ForEach(
                    addedOrEditedLocalFiles,
                    (localPath) =>
                    {
                        string gitPath = localPath.Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
                        long offset;
                        if (this.indexEntryOffsets.TryGetValue(gitPath, out offset))
                        {
                            if (NativeMethods.TryStatFileAndUpdateIndex(this.tracer, gitPath, indexView, offset))
                            {
                                Interlocked.Increment(ref updatedEntriesFromDisk);
                            }
                            else
                            {
                                this.tracer.RelatedError($"{nameof(this.UpdateFileInformationFromDiskForFiles)}: Failed to update file information from disk for file {0}", gitPath);
                            }
                        }
                    });
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "UpdateIndexFileInformation", new EventMetadata() { { "UpdatedFromDisk", updatedEntriesFromDisk } }, Keywords.Telemetry);
            return updatedEntriesFromDisk > 0;
        }

        private bool UpdateFileInformationForAllEntries(MemoryMappedViewAccessor indexView, Index otherIndex, bool shouldAlsoTryPopulateFromDisk)
        {
            long updatedEntriesFromOtherIndex = 0;
            long updatedEntriesFromDisk = 0;

            using (MemoryMappedFile mmf = otherIndex.GetMemoryMappedFile())
            using (MemoryMappedViewAccessor otherIndexView = mmf.CreateViewAccessor())
            {
                Parallel.ForEach(
                    this.indexEntryOffsets,
                    entry =>
                    {
                        string currentIndexFilename = entry.Key;
                        long currentIndexOffset = entry.Value;
                        if (!IndexEntry.HasInitializedCTimeEntry(indexView, currentIndexOffset))
                        {
                            long otherIndexOffset;
                            if (otherIndex.indexEntryOffsets.TryGetValue(currentIndexFilename, out otherIndexOffset))
                            {
                                if (IndexEntry.HasInitializedCTimeEntry(otherIndexView, otherIndexOffset))
                                {
                                    IndexEntry currentIndexEntry = new IndexEntry(indexView, currentIndexOffset);
                                    IndexEntry otherIndexEntry = new IndexEntry(otherIndexView, otherIndexOffset);

                                    currentIndexEntry.CtimeSeconds = otherIndexEntry.CtimeSeconds;
                                    currentIndexEntry.CtimeNanosecondFraction = otherIndexEntry.CtimeNanosecondFraction;
                                    currentIndexEntry.MtimeSeconds = otherIndexEntry.MtimeSeconds;
                                    currentIndexEntry.MtimeNanosecondFraction = otherIndexEntry.MtimeNanosecondFraction;
                                    currentIndexEntry.Dev = otherIndexEntry.Dev;
                                    currentIndexEntry.Ino = otherIndexEntry.Ino;
                                    currentIndexEntry.Uid = otherIndexEntry.Uid;
                                    currentIndexEntry.Gid = otherIndexEntry.Gid;
                                    currentIndexEntry.Size = otherIndexEntry.Size;

                                    Interlocked.Increment(ref updatedEntriesFromOtherIndex);
                                }
                            }
                            else if (shouldAlsoTryPopulateFromDisk)
                            {
                                string localPath = FromGitRelativePathToDotnetFullPath(currentIndexFilename, this.repoRoot);

                                if (NativeMethods.TryStatFileAndUpdateIndex(this.tracer, localPath, indexView, entry.Value))
                                {
                                    Interlocked.Increment(ref updatedEntriesFromDisk);
                                }
                            }
                        }
                    });
            }

            this.tracer.RelatedEvent(
                EventLevel.Informational,
                "UpdateIndexFileInformation",
                new EventMetadata()
                {
                    { "UpdatedFromOtherIndex", updatedEntriesFromOtherIndex },
                    { "UpdatedFromDisk", updatedEntriesFromDisk }
                },
                Keywords.Telemetry);

            return (updatedEntriesFromOtherIndex > 0) || (updatedEntriesFromDisk > 0);
        }

        private void MoveUpdatedIndexToFinalLocation(bool shouldSignIndex)
        {
            if (shouldSignIndex)
            {
                using (ITracer activity = this.tracer.StartActivity("SignIndex", EventLevel.Informational, Keywords.Telemetry, metadata: null))
                {
                    using (FileStream fs = File.Open(this.updatedIndexPath, FileMode.Open, FileAccess.ReadWrite))
                    {
                        // Truncate the old hash off. The Index class is expected to preserve any existing hash.
                        fs.SetLength(fs.Length - 20);
                        using (HashingStream hashStream = new HashingStream(fs))
                        {
                            fs.Position = 0;
                            hashStream.CopyTo(Stream.Null);
                            byte[] hash = hashStream.Hash;

                            // The fs pointer is now where the old hash used to be. Perfect. :)
                            fs.Write(hash, 0, hash.Length);
                        }
                    }
                }
            }

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

            Directory.CreateDirectory(Path.GetDirectoryName(this.versionMarkerFile));
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

        private void ParseIndex(Stream indexStream)
        {
            byte[] buffer = new byte[40];
            indexStream.Position = 0;

            byte[] signature = new byte[4];
            indexStream.Read(signature, 0, 4);
            if (!Enumerable.SequenceEqual(MagicSignature, signature))
            {
                throw new InvalidDataException("Incorrect magic signature for index: " + string.Join(string.Empty, signature.Select(c => (char)c)));
            }

            this.IndexVersion = this.ReadUInt32(buffer, indexStream);

            if (this.IndexVersion < 2 || this.IndexVersion > 4)
            {
                throw new InvalidDataException("Unsupported index version: " + this.IndexVersion);
            }

            this.entryCount = this.ReadUInt32(buffer, indexStream);

            this.tracer.RelatedEvent(EventLevel.Informational, "IndexData", new EventMetadata() { { "Index", this.updatedIndexPath }, { "Version", this.IndexVersion }, { "entryCount", this.entryCount } }, Keywords.Telemetry);

            this.indexEntryOffsets = new Dictionary<string, long>((int)this.entryCount, GVFSPlatform.Instance.Constants.PathComparer);

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
                ushort pathLength = (ushort)(flags & 0xFFF);
                entryLength += pathLength;

                bool skipWorktree = false;
                if (isExtended && (this.IndexVersion > 2))
                {
                    ushort extendedFlags = this.ReadUInt16(buffer, indexStream);
                    skipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                    entryLength += 2;
                }

                if (this.IndexVersion == 4)
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
                if (headerByte < 0)
                {
                    throw new EndOfStreamException("Index file has been truncated.");
                }

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
        internal class IndexEntry
        {
            private const long UnixEpochMilliseconds = 116444736000000000;
            private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            private MemoryMappedViewAccessor indexView;

            public IndexEntry(MemoryMappedViewAccessor indexView, long offset)
            {
                this.indexView = indexView;
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
                dev = 16,
                ino = 20,
                uid = 28,
                gid = 32,
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
                    return this.ToDotnetTime(this.CtimeSeconds, this.CtimeNanosecondFraction);
                }

                set
                {
                    IndexEntryTime time = this.ToGitTime(value);
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
                    return this.ToDotnetTime(this.MtimeSeconds, this.MtimeNanosecondFraction);
                }

                set
                {
                    IndexEntryTime times = this.ToGitTime(value);
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

            public uint Dev
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.dev);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.dev, value);
                }
            }

            public uint Ino
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.ino);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.ino, value);
                }
            }

            public uint Uid
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.uid);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.uid, value);
                }
            }

            public uint Gid
            {
                get
                {
                    return this.ReadUInt32(EntryOffsets.gid);
                }

                set
                {
                    this.WriteUInt32(EntryOffsets.gid, value);
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

            public static bool HasInitializedCTimeEntry(MemoryMappedViewAccessor indexView, long offset)
            {
                return EndianHelper.Swap(indexView.ReadUInt32(offset + (long)EntryOffsets.ctimeSeconds)) != 0;
            }

            private uint ReadUInt32(EntryOffsets fromOffset)
            {
                return EndianHelper.Swap(this.indexView.ReadUInt32(this.Offset + (long)fromOffset));
            }

            private void WriteUInt32(EntryOffsets fromOffset, uint data)
            {
                this.indexView.Write(this.Offset + (long)fromOffset, EndianHelper.Swap(data));
            }

            private ushort ReadUInt16(EntryOffsets fromOffset)
            {
                return EndianHelper.Swap(this.indexView.ReadUInt16(this.Offset + (long)fromOffset));
            }

            private void WriteUInt16(EntryOffsets fromOffset, ushort data)
            {
                this.indexView.Write(this.Offset + (long)fromOffset, EndianHelper.Swap(data));
            }

            private IndexEntryTime ToGitTime(DateTime datetime)
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

            private DateTime ToDotnetTime(uint seconds, uint nanosecondFraction)
            {
                DateTime time = UnixEpoch.AddSeconds(seconds).AddMilliseconds(nanosecondFraction / 1000000);
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
