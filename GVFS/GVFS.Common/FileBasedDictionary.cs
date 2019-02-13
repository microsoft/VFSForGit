using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GVFS.Common
{
    public class FileBasedDictionary<TKey, TValue> : FileBasedCollection
    {
        private ConcurrentDictionary<TKey, TValue> data;

        private FileBasedDictionary(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string dataFilePath,
            IEqualityComparer<TKey> keyComparer)
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile: false)
        {
            this.data = new ConcurrentDictionary<TKey, TValue>(keyComparer);
        }

        public static bool TryCreate(
            ITracer tracer,
            string dictionaryPath,
            PhysicalFileSystem fileSystem,
            out FileBasedDictionary<TKey, TValue> output,
            out string error,
            IEqualityComparer<TKey> keyComparer = null)
        {
            output = new FileBasedDictionary<TKey, TValue>(
                tracer,
                fileSystem,
                dictionaryPath,
                keyComparer ?? EqualityComparer<TKey>.Default);

            if (!output.TryLoadFromDisk<TKey, TValue>(
                output.TryParseAddLine,
                output.TryParseRemoveLine,
                output.HandleAddLine,
                out error))
            {
                output = null;
                return false;
            }

            return true;
        }

        public void SetValuesAndFlush(IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            try
            {
                foreach (KeyValuePair<TKey, TValue> kvp in values)
                {
                    this.data[kvp.Key] = kvp.Value;
                }

                this.Flush();
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public void SetValueAndFlush(TKey key, TValue value)
        {
            try
            {
                this.data[key] = value;
                this.Flush();
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            try
            {
                return this.data.TryGetValue(key, out value);
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public void RemoveAndFlush(TKey key)
        {
            try
            {
                TValue value;
                if (this.data.TryRemove(key, out value))
                {
                    this.Flush();
                }
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        public Dictionary<TKey, TValue> GetAllKeysAndValues()
        {
            return new Dictionary<TKey, TValue>(this.data);
        }

        private void Flush()
        {
            this.WriteAndReplaceDataFile(this.GenerateDataLines);
        }

        private bool TryParseAddLine(string line, out TKey key, out TValue value, out string error)
        {
            try
            {
                KeyValuePair<TKey, TValue> kvp = JsonConvert.DeserializeObject<KeyValuePair<TKey, TValue>>(line);
                key = kvp.Key;
                value = kvp.Value;
            }
            catch (JsonException ex)
            {
                key = default(TKey);
                value = default(TValue);
                error = "Could not deserialize JSON for add line: " + ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        private bool TryParseRemoveLine(string line, out TKey key, out string error)
        {
            try
            {
                key = JsonConvert.DeserializeObject<TKey>(line);
            }
            catch (JsonException ex)
            {
                key = default(TKey);
                error = "Could not deserialize JSON for delete line: " + ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        private void HandleAddLine(TKey key, TValue value)
        {
            this.data.TryAdd(key, value);
        }

        private IEnumerable<string> GenerateDataLines()
        {
            foreach (KeyValuePair<TKey, TValue> kvp in this.data)
            {
                yield return this.FormatAddLine(JsonConvert.SerializeObject(kvp).Trim());
            }
        }
    }
}
