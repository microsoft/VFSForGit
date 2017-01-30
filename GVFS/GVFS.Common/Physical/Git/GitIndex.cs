using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.Common.Physical.Git
{
    public class GitIndex : IDisposable
    {
        private const ushort ExtendedBit = 0x4000;
        private const ushort SkipWorktreeBit = 0x4000;
        private const int BaseEntryLength = 62;
        private const int MaxPathBufferSize = 4096;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private Dictionary<string, long> pathOffsets;
        private bool pathOffsetsIsInvalid;
        private string indexPath;
        private string lockPath;
        private ITracer tracer;
        private Enlistment enlistment;
        private Stream indexFileStream;
        private FileBasedLock gitIndexLock;

        public GitIndex(ITracer tracer, Enlistment enlistment, string virtualIndexPath, string virtualIndexLockPath)
        {
            this.indexPath = virtualIndexPath;
            this.lockPath = virtualIndexLockPath;
            this.pathOffsetsIsInvalid = true;
            this.tracer = tracer;
            this.enlistment = enlistment;
        }

        public void Initialize()
        {
            this.gitIndexLock = new FileBasedLock(
                new PhysicalFileSystem(), 
                this.tracer, 
                this.lockPath, 
                "GVFS", 
                FileBasedLock.ExistingLockCleanup.DeleteExisting);
        }

        public CallbackResult Open()
        {
            if (!File.Exists(this.indexPath))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("ErrorMessage", "Can't open the index because it doesn't exist");
                this.tracer.RelatedError(metadata);

                return CallbackResult.FatalError;
            }

            if (!this.gitIndexLock.TryAcquireLockAndDeleteOnClose())
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                this.tracer.RelatedEvent(EventLevel.Verbose, "OpenCantAcquireIndexLock", metadata);

                return CallbackResult.RetryableError;
            }

            CallbackResult result = CallbackResult.FatalError;
            try
            {
                // TODO 667979: check if the index is missing and generate a new one if needed

                this.indexFileStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                result = CallbackResult.Success;
            }
            catch (IOException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "IOException in Open (RetryableError)");
                this.tracer.RelatedError(metadata);

                result = CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Exception in Open (FatalError)");
                this.tracer.RelatedError(metadata);

                result = CallbackResult.FatalError;
            }
            finally
            {
                if (result != CallbackResult.Success)
                {
                    if (!this.gitIndexLock.TryReleaseLock())
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", "GitIndex");
                        metadata.Add("ErrorMessage", "Unable to release index.lock in Open (FatalError)");
                        this.tracer.RelatedError(metadata);

                        result = CallbackResult.FatalError;
                    }
                }
            }

            return result;
        }

        public CallbackResult Close()
        {
            if (this.indexFileStream != null)
            {
                this.indexFileStream.Dispose();
                this.indexFileStream = null;
            }

            try
            {
                if (!this.gitIndexLock.IsOpen() ||
                    this.gitIndexLock.TryReleaseLock())
                {
                    return CallbackResult.Success;
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Fatal Exception in Close");
                this.tracer.RelatedError(metadata);
            }

            return CallbackResult.FatalError;
        }

        public virtual CallbackResult ClearSkipWorktreeAndUpdateEntry(string filePath, DateTime createTimeUtc, DateTime lastWriteTimeUtc, uint fileSize)
        {
            try
            {
                if (this.pathOffsetsIsInvalid)
                {
                    this.pathOffsetsIsInvalid = false;
                    this.ParseIndex();

                    if (this.pathOffsetsIsInvalid)
                    {
                        return CallbackResult.RetryableError;
                    }
                }

                string gitStyleFilePath = filePath.TrimStart(GVFSConstants.PathSeparator).Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator);
                long offset;
                if (this.pathOffsets.TryGetValue(gitStyleFilePath, out offset))
                {
                    if (createTimeUtc == DateTime.MinValue ||
                        lastWriteTimeUtc == DateTime.MinValue ||
                        fileSize == 0)
                    {
                        try
                        {
                            FileInfo fileInfo = new FileInfo(Path.Combine(this.enlistment.WorkingDirectoryRoot, filePath));
                            if (fileInfo.Exists)
                            {
                                createTimeUtc = fileInfo.CreationTimeUtc;
                                lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                                fileSize = (uint)fileInfo.Length;
                            }
                        }
                        catch (IOException e)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Area", "GitIndex");
                            metadata.Add("filePath", filePath);
                            metadata.Add("Exception", e.ToString());
                            metadata.Add("ErrorMessage", "IOException caught while trying to get FileInfo for index entry");
                            this.tracer.RelatedError(metadata);
                        }
                    }

                    uint ctimeSeconds = this.ToUnixEpochSeconds(createTimeUtc);
                    uint ctimeNanosecondFraction = this.ToUnixNanosecondFraction(createTimeUtc);
                    uint mtimeSeconds = this.ToUnixEpochSeconds(lastWriteTimeUtc);
                    uint mtimeNanosecondFraction = this.ToUnixNanosecondFraction(lastWriteTimeUtc);

                    this.indexFileStream.Seek(offset, SeekOrigin.Begin);

                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(ctimeSeconds)), 0, 4);            // ctime seconds
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(ctimeNanosecondFraction)), 0, 4); // ctime nanosecond fractions
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(mtimeSeconds)), 0, 4);            // mtime seconds
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(mtimeNanosecondFraction)), 0, 4); // mtime nanosecond fractions
                    this.indexFileStream.Seek(20, SeekOrigin.Current);                                                   // dev + ino + mode + uid + gid
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(fileSize)), 0, 4);                // size
                    this.indexFileStream.Seek(22, SeekOrigin.Current);                                                   // sha + flags
                    this.indexFileStream.Write(new byte[2] { 0, 0 }, 0, 2);                                              // extended flags
                    this.indexFileStream.Flush();

                    this.pathOffsets.Remove(gitStyleFilePath);
                }
            }
            catch (IOException e)
            {
                this.pathOffsetsIsInvalid = true;
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "IOException in ClearSkipWorktreeBitWhileHoldingIndexLock (RetryableError)");
                this.tracer.RelatedError(metadata);

                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                this.pathOffsetsIsInvalid = true;
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GitIndex");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Exception in ClearSkipWorktreeBitWhileHoldingIndexLock (FatalError)");
                this.tracer.RelatedError(metadata);

                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }

        public void Invalidate()
        {
            if (!this.gitIndexLock.IsOpen())
            {
                this.pathOffsetsIsInvalid = true;
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.gitIndexLock != null)
                {
                    this.gitIndexLock.Dispose();
                    this.gitIndexLock = null;
                }
            }
        }

        private uint ToUnixNanosecondFraction(DateTime datetime)
        {
            if (datetime > UnixEpoch)
            {
                TimeSpan timediff = datetime - UnixEpoch;
                double nanoseconds = (timediff.TotalSeconds - Math.Truncate(timediff.TotalSeconds)) * 1000000000;
                return Convert.ToUInt32(nanoseconds);
            }
            else
            {
                return 0;
            }
        }

        private uint ToUnixEpochSeconds(DateTime datetime)
        {
            if (datetime > UnixEpoch)
            {
                return Convert.ToUInt32(Math.Truncate((datetime - UnixEpoch).TotalSeconds));
            }
            else
            {
                return 0;
            }
        }

        private void ParseIndex()
        {
            this.pathOffsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            this.indexFileStream.Position = 0;
            using (BigEndianReader reader = new BigEndianReader(this.indexFileStream))
            {
                reader.ReadBytes(4);
                uint version = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();

                int previousPathLength = 0;
                byte[] pathBuffer = new byte[MaxPathBufferSize];
                for (int i = 0; i < entryCount; i++)
                {
                    // If the path offsets gets set as invalid we can bail
                    // since the index will have to be reparsed
                    if (this.pathOffsetsIsInvalid)
                    {
                        return;
                    }

                    long entryOffset = this.indexFileStream.Position;
                    int entryLength = BaseEntryLength;
                    reader.ReadBytes(60);

                    ushort flags = reader.ReadUInt16();
                    bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                    int pathLength = (ushort)((flags << 20) >> 20);
                    entryLength += pathLength;

                    bool skipWorktree = false;
                    if (isExtended)
                    {
                        ushort extendedFlags = reader.ReadUInt16();
                        skipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                        entryLength += 2;
                    }

                    if (version == 4)
                    {
                        int replaceLength = this.ReadReplaceLength(reader);
                        byte ch;
                        int index = previousPathLength - replaceLength;
                        while ((ch = reader.ReadByte()) != '\0')
                        {
                            if (index >= pathBuffer.Length)
                            {
                                throw new InvalidOperationException("Git index path entry too large.");
                            }

                            pathBuffer[index] = ch;
                            ++index;
                        }

                        previousPathLength = index;
                        if (skipWorktree)
                        {
                            this.pathOffsets[Encoding.UTF8.GetString(pathBuffer, 0, index)] = entryOffset;
                        }
                    }
                    else
                    {
                        byte[] path = reader.ReadBytes(pathLength);
                        int nullbytes = 8 - (entryLength % 8);
                        reader.ReadBytes(nullbytes);

                        if (skipWorktree)
                        {
                            this.pathOffsets[Encoding.UTF8.GetString(path)] = entryOffset;
                        }
                    }
                }
            }
        }

        private int ReadReplaceLength(BinaryReader reader)
        {
            int headerByte = reader.ReadByte();
            int offset = headerByte & 0x7f;

            // Terminate the loop when the high bit is no longer set.
            for (int i = 0; (headerByte & 0x80) != 0; i++)
            {
                headerByte = reader.ReadByte();

                offset += 1;
                offset = (offset << 7) + (headerByte & 0x7f);
            }

            return offset;
        }
    }
}