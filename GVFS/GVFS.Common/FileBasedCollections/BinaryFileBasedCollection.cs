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
    public abstract class BinaryFileBasedCollection<TEntry> : GenericFileBasedCollection<TEntry, Tuple<byte, TEntry>>
    {
        public static readonly byte[] EntryTerminator = new byte[] { 0, 0, 0, 0 };
        protected const byte AddEntryPrefix = 1 << 0;
        protected const byte RemoveEntryPrefix = 1 << 1;

        private readonly Action<BinaryWriter, TEntry> serializeEntry;

        protected BinaryFileBasedCollection(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string dataFilePath,
            bool collectionAppendsDirectlyToFile,
            Action<BinaryWriter, TEntry> serializeEntry)
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile)
        {
            this.serializeEntry = serializeEntry;
        }

        protected delegate bool TryParseAdd<TKey, TValue>(BinaryReader reader, out TKey key, out TValue value, out string error);
        protected delegate bool TryParseRemove<TKey>(BinaryReader reader, out TKey key, out string error);

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

        /// <summary>
        /// Reads entries from dataFileHandle, removing any data after the last NewLine ("\r\n"). Requires fileLock.
        /// </summary>
        protected override void RemoveLastEntryIfInvalid()
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

        protected override Tuple<byte, TEntry> FormatAddEntry(TEntry entry)
        {
            return new Tuple<byte, TEntry>(AddEntryPrefix, entry);
        }

        protected override Tuple<byte, TEntry> FormatRemoveEntry(TEntry entry)
        {
            return new Tuple<byte, TEntry>(RemoveEntryPrefix, entry);
        }

        protected override void WriteEntry(Stream stream, Tuple<byte, TEntry> dataEntry)
        {
            using (BinaryWriter w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                this.WriteEntry(w, dataEntry.Item1, dataEntry.Item2);
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