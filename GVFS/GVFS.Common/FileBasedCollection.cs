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
    public abstract class FileBasedCollection : GenericFileBasedCollection<string, string>
    {
        private const string EtwArea = nameof(FileBasedCollection);

        private const string AddEntryPrefix = "A ";
        private const string RemoveEntryPrefix = "D ";

        // Use the same newline separator regardless of platform
        private const string NewLine = "\r\n";

        protected FileBasedCollection(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath, bool collectionAppendsDirectlyToFile)
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile)
        {
        }

        protected delegate bool TryParseAdd<TKey, TValue>(string line, out TKey key, out TValue value, out string error);
        protected delegate bool TryParseRemove<TKey>(string line, out TKey key, out string error);

        protected override string FormatAddEntry(string line)
        {
            return AddEntryPrefix + line;
        }

        protected override string FormatRemoveEntry(string line)
        {
            return RemoveEntryPrefix + line;
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

        /// <summary>
        /// Reads entries from dataFileHandle, removing any data after the last NewLine ("\r\n"). Requires fileLock.
        /// </summary>
        protected override void RemoveLastEntryIfInvalid()
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

        protected override void WriteEntry(Stream stream, string dataLine)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(dataLine + NewLine);
            stream.Write(bytes, 0, bytes.Length);
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
