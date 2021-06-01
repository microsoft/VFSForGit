using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.Common
{
    public abstract class FileBasedCollection : IDisposable
    {
        private const string EtwArea = nameof(FileBasedCollection);

        private const string AddEntryPrefix = "A ";
        private const string RemoveEntryPrefix = "D ";

        // Use the same newline separator regardless of platform
        private const string NewLine = "\r\n";
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

        private Stream dataFileHandle;

        protected FileBasedCollection(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath, bool collectionAppendsDirectlyToFile)
        {
            this.Tracer = tracer;
            this.fileSystem = fileSystem;
            this.DataFilePath = dataFilePath;
            this.tempFilePath = this.DataFilePath + ".tmp";
            this.dataDirectoryPath = Path.GetDirectoryName(this.DataFilePath);
            this.collectionAppendsDirectlyToFile = collectionAppendsDirectlyToFile;
        }

        protected delegate bool TryParseAdd<TKey, TValue>(string line, out TKey key, out TValue value, out string error);
        protected delegate bool TryParseRemove<TKey>(string line, out TKey key, out string error);

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

        protected void WriteAndReplaceDataFile(Func<IEnumerable<string>> getDataLines)
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
                            tmpFileCreated = this.TryWriteTempFile(getDataLines, out lastException);
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

        protected string FormatAddLine(string line)
        {
            return AddEntryPrefix + line;
        }

        protected string FormatRemoveLine(string line)
        {
            return RemoveEntryPrefix + line;
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteAddEntry(string value, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                string line = this.FormatAddLine(value);
                if (synchronizedAction != null)
                {
                    synchronizedAction();
                }

                this.WriteToDisk(line);
            }
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteRemoveEntry(string key, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                string line = this.FormatRemoveLine(key);
                if (synchronizedAction != null)
                {
                    synchronizedAction();
                }

                this.WriteToDisk(line);
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
                    StreamReader reader = new StreamReader(this.dataFileHandle);
                    Dictionary<TKey, TValue> parsedEntries = new Dictionary<TKey, TValue>();
                    while (!reader.EndOfStream)
                    {
                        lineCount++;

                        // StreamReader strips the trailing /r/n
                        string line = reader.ReadLine();
                        if (line.StartsWith(RemoveEntryPrefix))
                        {
                            TKey key;
                            if (!tryParseRemove(line.Substring(RemoveEntryPrefix.Length), out key, out error))
                            {
                                error = string.Format("{0} is corrupt on line {1}: {2}", this.GetType().Name, lineCount, error);
                                return false;
                            }

                            parsedEntries.Remove(key);
                        }
                        else if (line.StartsWith(AddEntryPrefix))
                        {
                            TKey key;
                            TValue value;
                            if (!tryParseAdd(line.Substring(AddEntryPrefix.Length), out key, out value, out error))
                            {
                                error = string.Format("{0} is corrupt on line {1}: {2}", this.GetType().Name, lineCount, error);
                                return false;
                            }

                            parsedEntries[key] = value;
                        }
                        else
                        {
                            error = string.Format("{0} is corrupt on line {1}: Invalid Prefix '{2}'", this.GetType().Name, lineCount, line[0]);
                            return false;
                        }
                    }

                    foreach (KeyValuePair<TKey, TValue> kvp in parsedEntries)
                    {
                        add(kvp.Key, kvp.Value);
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
        private void WriteToDisk(string value)
        {
            if (!this.collectionAppendsDirectlyToFile)
            {
                throw new InvalidOperationException(nameof(this.WriteToDisk) + " requires that collectionAppendsDirectlyToFile be true");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value + NewLine);
            lock (this.fileLock)
            {
                this.dataFileHandle.Write(bytes, 0, bytes.Length);
                this.dataFileHandle.Flush();
            }
        }

        /// <summary>
        /// Reads entries from dataFileHandle, removing any data after the last NewLine ("\r\n"). Requires fileLock.
        /// </summary>
        private void RemoveLastEntryIfInvalid()
        {
            if (this.dataFileHandle.Length > 2)
            {
                this.dataFileHandle.Seek(-2, SeekOrigin.End);
                if (this.dataFileHandle.ReadByte() != '\r' ||
                    this.dataFileHandle.ReadByte() != '\n')
                {
                    this.dataFileHandle.Seek(0, SeekOrigin.Begin);
                    long lastLineEnding = 0;
                    while (this.dataFileHandle.Position < this.dataFileHandle.Length)
                    {
                        if (this.dataFileHandle.ReadByte() == '\r' && this.dataFileHandle.ReadByte() == '\n')
                        {
                            lastLineEnding = this.dataFileHandle.Position;
                        }
                    }

                    this.dataFileHandle.SetLength(lastLineEnding);
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
        private bool TryWriteTempFile(Func<IEnumerable<string>> getDataLines, out Exception handledException)
        {
            handledException = null;

            try
            {
                using (Stream tempFile = this.fileSystem.OpenFileStream(this.tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, callFlushFileBuffers: true))
                using (StreamWriter writer = new StreamWriter(tempFile))
                {
                    foreach (string line in getDataLines())
                    {
                        writer.Write(line + NewLine);
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
    }
}
