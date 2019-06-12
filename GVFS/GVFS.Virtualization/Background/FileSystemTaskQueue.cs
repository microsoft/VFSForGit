using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Virtualization.Background
{
    public class FileSystemTaskQueue : FileBasedCollection
    {
        private const string ValueTerminator = "\0";
        private const char ValueTerminatorChar = '\0';

        private readonly ConcurrentQueue<KeyValuePair<long, FileSystemTask>> data = new ConcurrentQueue<KeyValuePair<long, FileSystemTask>>();

        private long entryCounter = 0;

        private FileSystemTaskQueue(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath)
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile: true)
        {
        }

        public bool IsEmpty
        {
            get { return this.data.IsEmpty; }
        }

        /// <summary>
        /// Gets the count of tasks in the queue
        /// </summary>
        /// <remarks>
        /// This is an expensive call on .net core and you should avoid calling in performance critical paths.
        /// Use the IsEmpty property when checking if the queue has any items instead of Count.
        /// </remarks>
        public int Count
        {
            get { return this.data.Count; }
        }

        public static bool TryCreate(ITracer tracer, string dataDirectory, PhysicalFileSystem fileSystem, out FileSystemTaskQueue output, out string error)
        {
            output = new FileSystemTaskQueue(tracer, fileSystem, dataDirectory);
            if (!output.TryLoadFromDisk<long, FileSystemTask>(
                output.TryParseAddLine,
                output.TryParseRemoveLine,
                output.AddParsedEntry,
                out error))
            {
                output = null;
                return false;
            }

            return true;
        }

        public void EnqueueAndFlush(FileSystemTask value)
        {
            try
            {
                KeyValuePair<long, FileSystemTask> kvp = new KeyValuePair<long, FileSystemTask>(
                    Interlocked.Increment(ref this.entryCounter),
                    value);

                this.WriteAddEntry(
                    kvp.Key + ValueTerminator + this.Serialize(kvp.Value),
                    () => this.data.Enqueue(kvp));
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public void DequeueAndFlush(FileSystemTask expectedValue)
        {
            try
            {
                KeyValuePair<long, FileSystemTask> kvp;
                if (this.data.TryDequeue(out kvp))
                {
                    if (!expectedValue.Equals(kvp.Value))
                    {
                        throw new InvalidOperationException(string.Format("Dequeued value is expected to be the same as input value. Expected: '{0}' Actual: '{1}'", expectedValue, kvp.Value));
                    }

                    this.WriteRemoveEntry(kvp.Key.ToString());

                    this.DeleteDataFileIfCondition(() => this.data.IsEmpty);
                }
                else
                {
                    throw new InvalidOperationException(string.Format("Dequeued value is expected to be the same as input value. Expected: '{0}' Actual: 'None. List is empty.'", expectedValue));
                }
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public bool TryPeek(out FileSystemTask value)
        {
            try
            {
                KeyValuePair<long, FileSystemTask> kvp;
                if (this.data.TryPeek(out kvp))
                {
                    value = kvp.Value;
                    return true;
                }

                value = default(FileSystemTask);
                return false;
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private bool TryParseAddLine(string line, out long key, out FileSystemTask value, out string error)
        {
            // Expected: <ID>\0<Background Update>
            int idx = line.IndexOf(ValueTerminator, StringComparison.Ordinal);
            if (idx < 0)
            {
                key = 0;
                value = default(FileSystemTask);
                error = "Add line missing ID terminator: " + line;
                return false;
            }

            if (!long.TryParse(line.Substring(0, idx), out key))
            {
                value = default(FileSystemTask);
                error = "Could not parse ID for add line: " + line;
                return false;
            }

            if (!this.TryDeserialize(line.Substring(idx + 1), out value))
            {
                value = default(FileSystemTask);
                error = $"Could not parse {nameof(FileSystemTask)} for add line: " + line;
                return false;
            }

            error = null;
            return true;
        }

        private string Serialize(FileSystemTask input)
        {
            return ((int)input.Operation) + ValueTerminator + input.VirtualPath + ValueTerminator + input.OldVirtualPath;
        }

        private bool TryDeserialize(string line, out FileSystemTask value)
        {
            // Expected: <Operation>\0<Virtual Path>\0<Old Virtual Path>
            string[] parts = line.Split(ValueTerminatorChar);
            if (parts.Length != 3)
            {
                value = default(FileSystemTask);
                return false;
            }

            FileSystemTask.OperationType operationType;
            if (!Enum.TryParse(parts[0], out operationType))
            {
                value = default(FileSystemTask);
                return false;
            }

            value = new FileSystemTask(
                operationType,
                parts[1],
                parts[2]);

            return true;
        }

        private bool TryParseRemoveLine(string line, out long key, out string error)
        {
            if (!long.TryParse(line, out key))
            {
                error = "Could not parse ID for remove line: " + line;
                return false;
            }

            error = null;
            return true;
        }

        private void AddParsedEntry(long key, FileSystemTask value)
        {
            this.data.Enqueue(new KeyValuePair<long, FileSystemTask>(key, value));
            if (this.entryCounter < key + 1)
            {
                this.entryCounter = key;
            }
        }
    }
}
