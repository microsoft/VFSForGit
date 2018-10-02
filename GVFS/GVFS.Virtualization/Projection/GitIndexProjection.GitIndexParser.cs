using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Background;
using System;
using System.IO;
using System.Linq;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal partial class GitIndexParser
        {
            public const int PageSize = 512 * 1024;

            private const ushort ExtendedBit = 0x4000;
            private const ushort SkipWorktreeBit = 0x4000;

            private Stream indexStream;
            private byte[] page;
            private int nextByteIndex;

            private GitIndexProjection projection;

            /// <summary>
            /// A single GitIndexEntry instance used for parsing all entries in the index
            /// </summary>
            private GitIndexEntry resuableParsedIndexEntry = new GitIndexEntry();

            public GitIndexParser(GitIndexProjection projection)
            {
                this.projection = projection;
                this.page = new byte[PageSize];
            }

            public enum MergeStage : byte
            {
                NoConflicts = 0,
                CommonAncestor = 1,
                Yours = 2,
                Theirs = 3
            }

            public static void ValidateIndex(ITracer tracer, Stream indexStream)
            {
                GitIndexParser indexParser = new GitIndexParser(null);
                FileSystemTaskResult result = indexParser.ParseIndex(tracer, indexStream, ValidateIndexEntry);

                if (result != FileSystemTaskResult.Success)
                {
                    // ValidateIndex should always result in FileSystemTaskResult.Success (or a thrown exception)
                    throw new InvalidOperationException($"{nameof(ValidateIndex)} failed: {result.ToString()}");
                }
            }

            public void RebuildProjection(ITracer tracer, Stream indexStream)
            {
                if (this.projection == null)
                {
                    throw new InvalidOperationException($"{nameof(this.projection)} cannot be null when calling {nameof(RebuildProjection)}");
                }

                this.projection.ClearProjectionCaches();
                FileSystemTaskResult result = this.ParseIndex(tracer, indexStream, this.AddToProjection);
                if (result != FileSystemTaskResult.Success)
                {
                    // RebuildProjection should always result in FileSystemTaskResult.Success (or a thrown exception)
                    throw new InvalidOperationException($"{nameof(RebuildProjection)}: {nameof(GitIndexParser.ParseIndex)} failed to {nameof(this.AddToProjection)}");
                }
            }

            public FileSystemTaskResult AddMissingModifiedFiles(ITracer tracer, Stream indexStream)
            {
                if (this.projection == null)
                {
                    throw new InvalidOperationException($"{nameof(this.projection)} cannot be null when calling {nameof(AddMissingModifiedFiles)}");
                }

                return this.ParseIndex(tracer, indexStream, this.AddToModifiedFiles);
            }

            private static FileSystemTaskResult ValidateIndexEntry(GitIndexEntry data)
            {
                if (data.PathLength <= 0 || data.PathBuffer[0] == 0)
                {
                    throw new InvalidDataException("Zero-length path found in index");
                }

                return FileSystemTaskResult.Success;
            }

            private FileSystemTaskResult AddToProjection(GitIndexEntry data)
            {
                // Never want to project the common ancestor even if the skip worktree bit is on
                if ((data.MergeState != MergeStage.CommonAncestor && data.SkipWorktree) || data.MergeState == MergeStage.Yours)
                {
                    data.ParsePath();
                    this.projection.AddItemFromIndexEntry(data);
                }
                else
                {
                    data.ClearLastParent();
                }

                return FileSystemTaskResult.Success;
            }

            private FileSystemTaskResult AddToModifiedFiles(GitIndexEntry data)
            {
                if (!data.SkipWorktree)
                {
                    // A git command (e.g. 'git reset --mixed') may have cleared a file's skip worktree bit without
                    // triggering an update to the projection.  Ensure this file is in GVFS's modified files database
                    data.ParsePath();
                    return this.projection.AddModifiedPath(data.GetFullPath());
                }
                else
                {
                    data.ClearLastParent();
                }

                return FileSystemTaskResult.Success;
            }

            /// <summary>
            /// Takes an action on a GitIndexEntry using the index in indexStream
            /// </summary>
            /// <param name="indexStream">Stream for reading a git index file</param>
            /// <param name="entryAction">Action to take on each GitIndexEntry from the index</param>
            /// <returns>
            /// FileSystemTaskResult indicating success or failure of the specified action
            /// </returns>
            /// <remarks>
            /// Only the AddToModifiedFiles method because it updates the modified paths file can result
            /// in TryIndexAction returning a FileSystemTaskResult other than Success.  All other actions result in success (or an exception in the
            /// case of a corrupt index)
            /// </remarks>
            private FileSystemTaskResult ParseIndex(ITracer tracer, Stream indexStream, Func<GitIndexEntry, FileSystemTaskResult> entryAction)
            {
                this.indexStream = indexStream;
                this.indexStream.Position = 0;
                this.ReadNextPage();

                if (this.page[0] != 'D' ||
                    this.page[1] != 'I' ||
                    this.page[2] != 'R' ||
                    this.page[3] != 'C')
                {
                    throw new InvalidDataException("Incorrect magic signature for index: " + string.Join(string.Empty, this.page.Take(4).Select(c => (char)c)));
                }

                this.Skip(4);
                uint indexVersion = this.ReadFromIndexHeader();
                if (indexVersion != 4)
                {
                    throw new InvalidDataException("Unsupported index version: " + indexVersion);
                }

                uint entryCount = this.ReadFromIndexHeader();

                // Don't want to flood the logs on large indexes so only log every 500ms
                const int LoggingTicksThreshold = 5000000;
                long nextLogTicks = DateTime.UtcNow.Ticks + LoggingTicksThreshold;

                SortedFolderEntries.InitializePools(tracer, entryCount);
                LazyUTF8String.InitializePools(tracer, entryCount);

                this.resuableParsedIndexEntry.ClearLastParent();
                int previousPathLength = 0;

                bool parseMode = GVFSPlatform.Instance.FileSystem.SupportsFileMode;
                FileSystemTaskResult result = FileSystemTaskResult.Success;
                for (int i = 0; i < entryCount; i++)
                {
                    if (parseMode)
                    {
                        this.Skip(26);

                        // 4-bit object type
                        //     valid values in binary are 1000(regular file), 1010(symbolic link) and 1110(gitlink)
                        // 3-bit unused
                        // 9-bit unix permission. Only 0755 and 0644 are valid for regular files. (Legacy repos can also contain 664)
                        //     Symbolic links and gitlinks have value 0 in this field.
                        ushort indexFormatTypeAndMode = this.ReadUInt16();

                        FileTypeAndMode typeAndMode = new FileTypeAndMode(indexFormatTypeAndMode);

                        switch (typeAndMode.Type)
                        {
                            case FileType.Regular:
                                if (typeAndMode.Mode != FileMode755 && 
                                    typeAndMode.Mode != FileMode644 && 
                                    typeAndMode.Mode != FileMode664)
                                {
                                    throw new InvalidDataException($"Invalid file mode {typeAndMode.GetModeAsOctalString()} found for regular file in index");
                                }

                                break;

                            case FileType.SymLink:
                            case FileType.GitLink:
                                if (typeAndMode.Mode != 0)
                                {
                                    throw new InvalidDataException($"Invalid file mode {typeAndMode.GetModeAsOctalString()} found for link file({typeAndMode.Type:X}) in index");
                                }

                                break;

                            default:
                                throw new InvalidDataException($"Invalid file type {typeAndMode.Type:X} found in index");
                        }
                                    
                        this.resuableParsedIndexEntry.TypeAndMode = typeAndMode;

                        this.Skip(12);
                    }
                    else
                    {
                        this.Skip(40);
                    }

                    this.ReadSha(this.resuableParsedIndexEntry);

                    ushort flags = this.ReadUInt16();
                    if (flags == 0)
                    {
                        throw new InvalidDataException("Invalid flags found in index");
                    }

                    this.resuableParsedIndexEntry.MergeState = (MergeStage)((flags >> 12) & 3);
                    bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                    this.resuableParsedIndexEntry.PathLength = (ushort)(flags & 0xFFF);

                    this.resuableParsedIndexEntry.SkipWorktree = false;
                    if (isExtended)
                    {
                        ushort extendedFlags = this.ReadUInt16();
                        this.resuableParsedIndexEntry.SkipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                    }

                    int replaceLength = this.ReadReplaceLength();
                    this.resuableParsedIndexEntry.ReplaceIndex = previousPathLength - replaceLength;
                    int bytesToRead = this.resuableParsedIndexEntry.PathLength - this.resuableParsedIndexEntry.ReplaceIndex + 1;
                    this.ReadPath(this.resuableParsedIndexEntry, this.resuableParsedIndexEntry.ReplaceIndex, bytesToRead);
                    previousPathLength = this.resuableParsedIndexEntry.PathLength;

                    result = entryAction.Invoke(this.resuableParsedIndexEntry);
                    if (result != FileSystemTaskResult.Success)
                    {
                        return result;
                    }

                    if (DateTime.UtcNow.Ticks > nextLogTicks)
                    {
                        tracer.RelatedInfo($"{i}/{entryCount} index entries parsed.");
                        nextLogTicks = DateTime.UtcNow.Ticks + LoggingTicksThreshold;
                    }
                }

                tracer.RelatedInfo($"Finished parsing {entryCount} index entries.");
                return result;
            }

            private void ReadNextPage()
            {
                this.indexStream.Read(this.page, 0, PageSize);
                this.nextByteIndex = 0;
            }

            private int ReadReplaceLength()
            {
                int headerByte = this.ReadByte();
                int offset = headerByte & 0x7f;

                // Terminate the loop when the high bit is no longer set.
                for (int i = 0; (headerByte & 0x80) != 0; i++)
                {
                    headerByte = this.ReadByte();
                    if (headerByte < 0)
                    {
                        throw new EndOfStreamException("Unexpected end of stream while reading git index.");
                    }

                    offset += 1;
                    offset = (offset << 7) + (headerByte & 0x7f);
                }

                return offset;
            }

            private void ReadSha(GitIndexEntry indexEntryData)
            {
                if (this.nextByteIndex + 20 <= PageSize)
                {
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, 0, 20);
                    this.Skip(20);
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = 20 - availableBytes;

                    if (availableBytes > 0)
                    {
                        Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, 0, availableBytes);
                    }

                    this.ReadNextPage();
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, availableBytes, remainingBytes);
                    this.Skip(remainingBytes);
                }
            }

            private void ReadPath(GitIndexEntry indexEntryData, int replaceIndex, int byteCount)
            {
                if (this.nextByteIndex + byteCount <= PageSize)
                {
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.PathBuffer, replaceIndex, byteCount);
                    this.Skip(byteCount);
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = byteCount - availableBytes;

                    if (availableBytes != 0)
                    {
                        this.ReadPath(indexEntryData, replaceIndex, availableBytes);
                    }

                    this.ReadNextPage();
                    this.ReadPath(indexEntryData, replaceIndex + availableBytes, remainingBytes);
                }
            }

            private uint ReadFromIndexHeader()
            {
                // This code should only get called for parsing the header, so we don't need to worry about wrapping around a page
                uint result = (uint)
                    (this.page[this.nextByteIndex] << 24 |
                    this.page[this.nextByteIndex + 1] << 16 |
                    this.page[this.nextByteIndex + 2] << 8 |
                    this.page[this.nextByteIndex + 3]);
                this.Skip(4);
                return result;
            }

            private ushort ReadUInt16()
            {
                if (this.nextByteIndex + 2 <= PageSize)
                {
                    ushort result = (ushort)
                        (this.page[this.nextByteIndex] << 8 |
                        this.page[this.nextByteIndex + 1]);
                    this.Skip(2);

                    return result;
                }
                else
                {
                    return (ushort)(this.ReadByte() << 8 | this.ReadByte());
                }
            }

            private byte ReadByte()
            {
                if (this.nextByteIndex < PageSize)
                {
                    byte result = this.page[this.nextByteIndex];

                    this.nextByteIndex++;

                    return result;
                }
                else
                {
                    this.ReadNextPage();
                    return this.ReadByte();
                }
            }

            private void Skip(int byteCount)
            {
                if (this.nextByteIndex + byteCount <= PageSize)
                {
                    this.nextByteIndex += byteCount;
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = byteCount - availableBytes;

                    this.ReadNextPage();
                    this.Skip(remainingBytes);
                }
            }
        }
    }
}
