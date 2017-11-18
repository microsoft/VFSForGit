using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace RGFS.GVFlt
{
    public class BackgroundGitUpdateQueue : FileBasedCollection
    {
        private const string ValueTerminator = "\0";
        private const char ValueTerminatorChar = '\0';

        private readonly ConcurrentQueue<KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate>> data = new ConcurrentQueue<KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate>>();
        
        private long entryCounter = 0;

        private BackgroundGitUpdateQueue(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath) 
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile: true)
        {
        }

        public int Count
        {
            get { return this.data.Count; }
        }
        
        public static bool TryCreate(ITracer tracer, string dataDirectory, PhysicalFileSystem fileSystem, out BackgroundGitUpdateQueue output, out string error)
        {
            output = new BackgroundGitUpdateQueue(tracer, fileSystem, dataDirectory);
            if (!output.TryLoadFromDisk<long, GVFltCallbacks.BackgroundGitUpdate>(
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

        public void EnqueueAndFlush(GVFltCallbacks.BackgroundGitUpdate value)
        {
            try
            {
                KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate> kvp = new KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate>(
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

        public void DequeueAndFlush(GVFltCallbacks.BackgroundGitUpdate expectedValue)
        {
            try
            {
                KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate> kvp;
                if (this.data.TryDequeue(out kvp))
                {
                    if (!expectedValue.Equals(kvp.Value))
                    {
                        throw new InvalidOperationException(string.Format("Dequeued value is expected to be the same as input value. Expected: '{0}' Actual: '{1}'", expectedValue, kvp.Value));
                    }

                    this.WriteRemoveEntry(kvp.Key.ToString());

                    this.DeleteDataFileIfCondition(() => this.data.Count == 0);
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

        public bool TryPeek(out GVFltCallbacks.BackgroundGitUpdate value)
        {
            try
            {
                KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate> kvp;
                if (this.data.TryPeek(out kvp))
                {
                    value = kvp.Value;
                    return true;
                }

                value = default(GVFltCallbacks.BackgroundGitUpdate);
                return false;
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private bool TryParseAddLine(string line, out long key, out GVFltCallbacks.BackgroundGitUpdate value, out string error)
        {
            // Expected: <ID>\0<Background Update>
            int idx = line.IndexOf(ValueTerminator);
            if (idx < 0)
            {
                key = 0;
                value = default(GVFltCallbacks.BackgroundGitUpdate);
                error = "Add line missing ID terminator: " + line;
                return false;
            }

            if (!long.TryParse(line.Substring(0, idx), out key))
            {
                value = default(GVFltCallbacks.BackgroundGitUpdate);
                error = "Could not parse ID for add line: " + line;
                return false;
            }

            if (!this.TryDeserialize(line.Substring(idx + 1), out value))
            {
                value = default(GVFltCallbacks.BackgroundGitUpdate);
                error = "Could not parse BackgroundGitUpdate for add line: " + line;
                return false;
            }

            error = null;
            return true;
        }

        private string Serialize(GVFltCallbacks.BackgroundGitUpdate input)
        {
            return ((int)input.Operation) + ValueTerminator + input.VirtualPath + ValueTerminator + input.OldVirtualPath;
        }

        private bool TryDeserialize(string line, out GVFltCallbacks.BackgroundGitUpdate value)
        {
            // Expected: <Operation>\0<Virtual Path>\0<Old Virtual Path>
            string[] parts = line.Split(ValueTerminatorChar);
            if (parts.Length != 3)
            {
                value = default(GVFltCallbacks.BackgroundGitUpdate);
                return false;
            }

            GVFltCallbacks.BackgroundGitUpdate.OperationType operationType;
            if (!Enum.TryParse(parts[0], out operationType))
            {
                value = default(GVFltCallbacks.BackgroundGitUpdate);
                return false;
            }
            
            value = new GVFltCallbacks.BackgroundGitUpdate(
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

        private void AddParsedEntry(long key, GVFltCallbacks.BackgroundGitUpdate value)
        {
            this.data.Enqueue(new KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate>(key, value));
            if (this.entryCounter < key + 1)
            {
                this.entryCounter = key;
            }
        }
    }
}
