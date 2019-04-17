using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GVFS.Common.FileBasedCollections
{
    public abstract class BinaryFileBasedCollection<TEntry> : IDisposable
    {
        public static readonly byte[] EntryTerminator = new byte[] { 0, 0, 0, 0 };
        protected const byte AddEntryPrefix = 1 << 0;
        protected const byte RemoveEntryPrefix = 1 << 1;
        private const string EtwArea = nameof(FileBasedCollection);

        // Use the same newline separator regardless of platform
        private const int IoFailureRetryDelayMS = 50;
        private const int IoFailureLoggingThreshold = 500;

        /// <summary>
        /// If true, this FileBasedCollection appends directly to dataFileHandle stream
        /// If false, this FileBasedCollection only using .tmp + rename to update data on disk
        /// </summary>
        private readonly bool collectionAppendsDirectlyToFile;

        private readonly object fileLock = new object();

        private readonly PhysicalFileSystem fileSystem;
        private readonly string dataDirectoryPath;
        private readonly string tempFilePath;

        private readonly Action<BinaryWriter, TEntry> serializeEntry;

        private Stream dataFileHandle;

        protected BinaryFileBasedCollection(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string dataFilePath,
            bool collectionAppendsDirectlyToFile,
            Action<BinaryWriter, TEntry> serializeEntry)
        {
            this.Tracer = tracer;
            this.fileSystem = fileSystem;
            this.DataFilePath = dataFilePath;
            this.tempFilePath = this.DataFilePath + ".tmp";
            this.dataDirectoryPath = Path.GetDirectoryName(this.DataFilePath);
            this.collectionAppendsDirectlyToFile = collectionAppendsDirectlyToFile;
            this.serializeEntry = serializeEntry;
        }

        protected delegate bool TryParseAdd<TKey, TValue>(BinaryReader reader, out TKey key, out TValue value, out string error);
        protected delegate bool TryParseRemove<TKey>(BinaryReader reader, out TKey key, out string error);

        public string DataFilePath { get; }

        protected ITracer Tracer { get; }

        public void Dispose()
        {
            lock (this.fileLock)
            {
                this.CloseDataFile();
            }
        }

        public void ForceFlush()
        {
            if (this.dataFileHandle != null)
            {
                FileStream fs = this.dataFileHandle as FileStream;
                if (fs != null)
                {
                    fs.Flush(flushToDisk: true);
                }
            }
        }

        protected void WriteAndReplaceDataFile(Func<IEnumerable<Tuple<byte, TEntry>>> getDataElements)
        {
            lock (this.fileLock)
            {
                try
                {
                    this.CloseDataFile();

                    bool tmpFileCreated = false;
                    int tmpFileCreateAttempts = 0;

                    bool tmpFileMoved = false;
                    int tmpFileMoveAttempts = 0;

                    Exception lastException = null;

                    while (!tmpFileCreated || !tmpFileMoved)
                    {
                        if (!tmpFileCreated)
                        {
                            tmpFileCreated = this.TryWriteTempFile(getDataElements, out lastException);
                            if (!tmpFileCreated)
                            {
                                if (this.Tracer != null && tmpFileCreateAttempts % IoFailureLoggingThreshold == 0)
                                {
                                    EventMetadata metadata = CreateEventMetadata(lastException);
                                    metadata.Add("tmpFileCreateAttempts", tmpFileCreateAttempts);
                                    this.Tracer.RelatedWarning(metadata, nameof(this.WriteAndReplaceDataFile) + ": Failed to create tmp file ... retrying");
                                }

                                ++tmpFileCreateAttempts;
                                Thread.Sleep(IoFailureRetryDelayMS);
                            }
                        }

                        if (tmpFileCreated)
                        {
                            try
                            {
                                if (this.fileSystem.FileExists(this.tempFilePath))
                                {
                                    this.fileSystem.MoveAndOverwriteFile(this.tempFilePath, this.DataFilePath);
                                    tmpFileMoved = true;
                                }
                                else
                                {
                                    if (this.Tracer != null)
                                    {
                                        EventMetadata metadata = CreateEventMetadata();
                                        metadata.Add("tmpFileMoveAttempts", tmpFileMoveAttempts);
                                        this.Tracer.RelatedWarning(metadata, nameof(this.WriteAndReplaceDataFile) + ": tmp file is missing. Recreating tmp file.");
                                    }

                                    tmpFileCreated = false;
                                }
                            }
                            catch (Win32Exception e)
                            {
                                if (this.Tracer != null && tmpFileMoveAttempts % IoFailureLoggingThreshold == 0)
                                {
                                    EventMetadata metadata = CreateEventMetadata(e);
                                    metadata.Add("tmpFileMoveAttempts", tmpFileMoveAttempts);
                                    this.Tracer.RelatedWarning(metadata, nameof(this.WriteAndReplaceDataFile) + ": Failed to overwrite data file ... retrying");
                                }

                                ++tmpFileMoveAttempts;
                                Thread.Sleep(IoFailureRetryDelayMS);
                            }
                        }
                    }

                    if (this.collectionAppendsDirectlyToFile)
                    {
                        this.OpenOrCreateDataFile(retryUntilSuccess: true);
                    }
                }
                catch (Exception e)
                {
                    throw new FileBasedCollectionException(e);
                }
            }
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteAddEntry(TEntry value, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                if (synchronizedAction != null)
                {
                    synchronizedAction();
                }

                this.WriteToDisk(AddEntryPrefix, value);
            }
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteRemoveEntry(TEntry key, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                if (synchronizedAction != null)
                {
                    synchronizedAction();
                }

                this.WriteToDisk(RemoveEntryPrefix, key);
            }
        }

        protected void DeleteDataFileIfCondition(Func<bool> condition)
        {
            if (!this.collectionAppendsDirectlyToFile)
            {
                throw new InvalidOperationException(nameof(this.DeleteDataFileIfCondition) + " requires that collectionAppendsDirectlyToFile be true");
            }

            lock (this.fileLock)
            {
                if (condition())
                {
                    this.dataFileHandle.SetLength(0);
                }
            }
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken</param>
        protected bool TryLoadFromDisk<TKey, TValue>(
            TryParseAdd<TKey, TValue> tryParseAdd,
            TryParseRemove<TKey> tryParseRemove,
            Action<TKey, TValue> add,
            Action<TKey> remove,
            out string error,
            Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                try
                {
                    if (synchronizedAction != null)
                    {
                        synchronizedAction();
                    }

                    this.fileSystem.CreateDirectory(this.dataDirectoryPath);

                    this.OpenOrCreateDataFile(retryUntilSuccess: false);

                    if (this.collectionAppendsDirectlyToFile)
                    {
                        this.RemoveLastEntryIfInvalid();
                    }

                    long lineCount = 0;

                    this.dataFileHandle.Seek(0, SeekOrigin.Begin);
                    BinaryReader reader = new BinaryReader(this.dataFileHandle, Encoding.UTF8, true);
                    while (this.dataFileHandle.Position < this.dataFileHandle.Length)
                    {
                        try
                        {
                            lineCount++;

                            // StreamReader strips the trailing /r/n
                            byte prefix = reader.ReadByte();
                            if (prefix == RemoveEntryPrefix)
                            {
                                TKey key;
                                if (!tryParseRemove(reader, out key, out error))
                                {
                                    error = string.Format("{0} is corrupt on line {1}: {2}", this.GetType().Name, lineCount, error);
                                    return false;
                                }

                                remove(key);
                            }
                            else if (prefix == AddEntryPrefix)
                            {
                                TKey key;
                                TValue value;
                                if (!tryParseAdd(reader, out key, out value, out error))
                                {
                                    error = string.Format("{0} is corrupt on line {1}: {2}", this.GetType().Name, lineCount, error);
                                    return false;
                                }

                                add(key, value);
                            }
                            else
                            {
                                error = string.Format("{0} is corrupt on line {1}: Invalid Prefix '{2}'", this.GetType().Name, lineCount, prefix);
                                return false;
                            }

                            byte[] terminatingBytes = reader.ReadBytes(4);
                            if (terminatingBytes.Length != 4 || !Array.TrueForAll(terminatingBytes, b => b == 0))
                            {
                                error = string.Format("{0} is corrupt on line {1}: Invalid entry suffix '{2}'", this.GetType().Name, lineCount, terminatingBytes.Aggregate(string.Empty, (s, b) => s+= b + " ", s => s.TrimEnd()));
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            // Nothing to do here, we've reached EOS
                        }
                    }

                    if (!this.collectionAppendsDirectlyToFile)
                    {
                        this.CloseDataFile();
                    }
                }
                catch (IOException ex)
                {
                    error = ex.ToString();
                    this.CloseDataFile();
                    return false;
                }
                catch (Exception e)
                {
                    this.CloseDataFile();
                    throw new FileBasedCollectionException(e);
                }

                error = null;
                return true;
            }
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        /// <summary>
        /// Closes dataFileHandle. Requires fileLock.
        /// </summary>
        private void CloseDataFile()
        {
            if (this.dataFileHandle != null)
            {
                this.dataFileHandle.Dispose();
                this.dataFileHandle = null;
            }
        }

        /// <summary>
        /// Opens dataFileHandle for ReadWrite. Requires fileLock.
        /// </summary>
        /// <param name="retryUntilSuccess">If true, OpenOrCreateDataFile will continue to retry until it succeeds</param>
        /// <remarks>If retryUntilSuccess is true, OpenOrCreateDataFile will only attempt to retry when the error is non-fatal</remarks>
        private void OpenOrCreateDataFile(bool retryUntilSuccess)
        {
            int attempts = 0;
            Exception lastException = null;
            while (true)
            {
                try
                {
                    if (this.dataFileHandle == null)
                    {
                        this.dataFileHandle = this.fileSystem.OpenFileStream(
                            this.DataFilePath,
                            FileMode.OpenOrCreate,
                            this.collectionAppendsDirectlyToFile ? FileAccess.ReadWrite : FileAccess.Read,
                            FileShare.Read,
                            callFlushFileBuffers: false);
                    }

                    this.dataFileHandle.Seek(0, SeekOrigin.End);
                    return;
                }
                catch (IOException e)
                {
                    lastException = e;
                }
                catch (UnauthorizedAccessException e)
                {
                    lastException = e;
                }

                if (retryUntilSuccess)
                {
                    if (this.Tracer != null && attempts % IoFailureLoggingThreshold == 0)
                    {
                        EventMetadata metadata = CreateEventMetadata(lastException);
                        metadata.Add("attempts", attempts);
                        this.Tracer.RelatedWarning(metadata, nameof(this.OpenOrCreateDataFile) + ": Failed to open data file stream ... retrying");
                    }

                    ++attempts;
                    Thread.Sleep(IoFailureRetryDelayMS);
                }
                else
                {
                    throw lastException;
                }
            }
        }

        /// <summary>
        /// Writes data as UTF8 to dataFileHandle. fileLock will be acquired.
        /// </summary>
        private void WriteToDisk(byte prefix, TEntry element)
        {
            if (!this.collectionAppendsDirectlyToFile)
            {
                throw new InvalidOperationException(nameof(this.WriteToDisk) + " requires that collectionAppendsDirectlyToFile be true");
            }

            lock (this.fileLock)
            {
                using (BinaryWriter w = new BinaryWriter(this.dataFileHandle, Encoding.UTF8, true))
                {
                    this.WriteEntry(w, prefix, element);
                }

                this.dataFileHandle.Flush();
            }
        }

        /// <summary>
        /// Reads entries from dataFileHandle, removing any data after the last NewLine ("\r\n"). Requires fileLock.
        /// </summary>
        private void RemoveLastEntryIfInvalid()
        {
            if (this.dataFileHandle.Length > EntryTerminator.Length)
            {
                this.dataFileHandle.Seek(0, SeekOrigin.End);
                this.dataFileHandle.Seek(EntryTerminator.Length * -1, SeekOrigin.Current);
                byte[] buffer = new byte[4];
                if (this.dataFileHandle.Read(buffer, 0, EntryTerminator.Length) != EntryTerminator.Length || !buffer.SequenceEqual(EntryTerminator))
                {
                    long lastTerminatorPosition = 0;
                    while (this.dataFileHandle.Position - (EntryTerminator.Length + 1) > 0)
                    {
                        this.dataFileHandle.Seek((EntryTerminator.Length + 1) * -1, SeekOrigin.Current);
                        if (this.dataFileHandle.Read(buffer, 0, EntryTerminator.Length) == EntryTerminator.Length && buffer.SequenceEqual(EntryTerminator))
                        {
                            lastTerminatorPosition = this.dataFileHandle.Position;
                        }
                    }

                    this.dataFileHandle.SetLength(lastTerminatorPosition);
                }
            }
        }

        /// <summary>
        /// Attempts to write all data lines to tmp file
        /// </summary>
        /// <param name="getDataLines">Method that returns the dataLines to write as an IEnumerable</param>
        /// <param name="handledException">Output parameter that's set when TryWriteTempFile catches a non-fatal exception</param>
        /// <returns>True if the write succeeded and false otherwise</returns>
        /// <remarks>If a fatal exception is encountered while trying to write the temp file, this method will not catch it.</remarks>
        private bool TryWriteTempFile(Func<IEnumerable<Tuple<byte, TEntry>>> getDataElemenets, out Exception handledException)
        {
            handledException = null;

            try
            {
                using (Stream tempFile = this.fileSystem.OpenFileStream(this.tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, callFlushFileBuffers: true))
                using (BinaryWriter writer = new BinaryWriter(tempFile))
                {
                    foreach ((byte prefix, TEntry element) in getDataElemenets())
                    {
                        this.WriteEntry(writer, prefix, element);
                    }

                    tempFile.Flush();
                }

                return true;
            }
            catch (IOException e)
            {
                handledException = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                handledException = e;
                return false;
            }
        }

        private void WriteEntry(BinaryWriter writer, byte prefix, TEntry entry)
        {
            writer.Write(prefix);
            this.serializeEntry(writer, entry);
            writer.Write(EntryTerminator);
        }
    }
}