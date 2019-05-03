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
    public abstract class GenericFileBasedCollection<TEntry, TDataEntry> : IDisposable
    {
        /// <summary>
        /// If true, this FileBasedCollection appends directly to dataFileHandle stream
        /// If false, this FileBasedCollection only using .tmp + rename to update data on disk
        /// </summary>
        protected readonly bool collectionAppendsDirectlyToFile;

        protected readonly object fileLock = new object();

        protected readonly PhysicalFileSystem fileSystem;
        protected readonly string dataDirectoryPath;
        protected readonly string tempFilePath;

        protected Stream dataFileHandle;

                private const string EtwArea = nameof(GenericFileBasedCollection<TEntry, TDataEntry>);

        private const int IoFailureRetryDelayMS = 50;
        private const int IoFailureLoggingThreshold = 500;

        protected GenericFileBasedCollection(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath, bool collectionAppendsDirectlyToFile)
        {
            this.Tracer = tracer;
            this.fileSystem = fileSystem;
            this.DataFilePath = dataFilePath;
            this.tempFilePath = this.DataFilePath + ".tmp";
            this.dataDirectoryPath = Path.GetDirectoryName(this.DataFilePath);
            this.collectionAppendsDirectlyToFile = collectionAppendsDirectlyToFile;
        }

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

        protected void WriteAndReplaceDataFile(Func<IEnumerable<TDataEntry>> geTDataEntrys)
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
                            tmpFileCreated = this.TryWriteTempFile(geTDataEntrys, out lastException);
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

        protected abstract TDataEntry FormatAddEntry(TEntry entry);
        protected abstract TDataEntry FormatRemoveEntry(TEntry entry);

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteAddEntry(TEntry value, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                TDataEntry line = this.FormatAddEntry(value);

                if (synchronizedAction != null)
                {
                    synchronizedAction();
                }

                this.WriteToDisk(line);
            }
        }

        /// <param name="synchronizedAction">An optional callback to be run as soon as the fileLock is taken.</param>
        protected void WriteRemoveEntry(TEntry key, Action synchronizedAction = null)
        {
            lock (this.fileLock)
            {
                TDataEntry line = this.FormatRemoveEntry(key);

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

        /// <summary>
        /// Closes dataFileHandle. Requires fileLock.
        /// </summary>
        protected void CloseDataFile()
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
        protected void OpenOrCreateDataFile(bool retryUntilSuccess)
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
        /// Reads entries from dataFileHandle, removing any data after the last entry terminator.
        /// </summary>
        protected abstract void RemoveLastEntryIfInvalid();

        protected abstract void WriteEntry(Stream stream, TDataEntry dataEntry);

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
        /// Attempts to write all data lines to tmp file
        /// </summary>
        /// <param name="getDataEntries">Method that returns the data entries to write as an IEnumerable</param>
        /// <param name="handledException">Output parameter that's set when TryWriteTempFile catches a non-fatal exception</param>
        /// <returns>True if the write succeeded and false otherwise</returns>
        /// <remarks>If a fatal exception is encountered while trying to write the temp file, this method will not catch it.</remarks>
        private bool TryWriteTempFile(Func<IEnumerable<TDataEntry>> getDataEntries, out Exception handledException)
        {
            handledException = null;

            try
            {
                using (Stream tempFile = this.fileSystem.OpenFileStream(this.tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, callFlushFileBuffers: true))
                {
                    foreach (TDataEntry entry in getDataEntries())
                    {
                        this.WriteEntry(tempFile, entry);
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

                /// <summary>
        /// Writes data to dataFileHandle. fileLock will be acquired.
        /// </summary>
        private void WriteToDisk(TDataEntry value)
        {
            if (!this.collectionAppendsDirectlyToFile)
            {
                throw new InvalidOperationException(nameof(this.WriteToDisk) + " requires that collectionAppendsDirectlyToFile be true");
            }

            lock (this.fileLock)
            {
                this.WriteEntry(this.dataFileHandle, value);
                this.dataFileHandle.Flush();
            }
        }
    }
}
