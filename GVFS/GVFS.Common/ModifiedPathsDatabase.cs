using System;
using System.Collections.Generic;
using System.IO;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;

namespace GVFS.Common
{
    /// <summary>
    /// The modified paths database is the list of files and folders that
    /// git is now responsible for keeping up to date. Files and folders are added
    /// to this list by being created, edited, deleted, or renamed.
    /// </summary>
    public class ModifiedPathsDatabase : FileBasedCollection
    {
        private ConcurrentHashSet<string> modifiedPaths;

        protected ModifiedPathsDatabase(ITracer tracer, PhysicalFileSystem fileSystem, string dataFilePath) 
            : base(tracer, fileSystem, dataFilePath, collectionAppendsDirectlyToFile: true)
        {
            this.modifiedPaths = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public int Count
        {
            get { return this.modifiedPaths.Count; }
        }

        public static bool TryLoadOrCreate(ITracer tracer, string dataDirectory, PhysicalFileSystem fileSystem, out ModifiedPathsDatabase output, out string error)
        {
            ModifiedPathsDatabase temp = new ModifiedPathsDatabase(tracer, fileSystem, dataDirectory);
            
            if (!temp.TryLoadFromDisk<string, string>(
                temp.TryParseAddLine,
                temp.TryParseRemoveLine,
                (key, value) => temp.modifiedPaths.Add(key),
                out error))
            {
                temp = null;
                output = null;
                return false;
            }

            if (temp.Count == 0)
            {
                bool isRetryable;
                temp.TryAdd(GVFSConstants.SpecialGitFiles.GitAttributes, isFolder: false, isRetryable: out isRetryable);
            }

            error = null;
            output = temp;
            return true;
        }

        public bool Contains(string path, bool isFolder)
        {
            string entry = this.NormalizeEntryString(path, isFolder);
            return this.modifiedPaths.Contains(entry);
        }

        public IEnumerable<string> GetAllModifiedPaths()
        {
            return this.modifiedPaths;
        }

        public bool TryAdd(string path, bool isFolder, out bool isRetryable)
        {
            isRetryable = true;
            string entry = this.NormalizeEntryString(path, isFolder);
            if (!this.modifiedPaths.Contains(entry))
            {
                try
                {
                    this.WriteAddEntry(entry, () => this.modifiedPaths.Add(entry));
                }
                catch (IOException e)
                {
                    this.TraceWarning(isFolder, entry, e, nameof(this.TryAdd));
                    return false;
                }
                catch (Exception e)
                {
                    this.TraceError(isFolder, entry, e, nameof(this.TryAdd));
                    isRetryable = false;
                    return false;
                }

                return true;
            }

            return true;
        }

        public bool TryRemove(string path, bool isFolder, out bool isRetryable)
        {
            isRetryable = true;
            string entry = this.NormalizeEntryString(path, isFolder);
            if (this.modifiedPaths.Contains(entry))
            {
                isRetryable = true;
                try
                {
                    this.WriteRemoveEntry(entry, () => this.modifiedPaths.TryRemove(entry));
                }
                catch (IOException e)
                {
                    this.TraceWarning(isFolder, entry, e, nameof(this.TryRemove));
                    return false;
                }
                catch (Exception e)
                {
                    this.TraceError(isFolder, entry, e, nameof(this.TryRemove));
                    isRetryable = false;
                    return false;
                }

                return true;
            }

            return true;
        }

        public void WriteAllEntriesAndFlush()
        {
            try
            {
                this.WriteAndReplaceDataFile(this.GenerateDataLines);
            }
            catch (Exception e)
            {
                throw new FileBasedCollectionException(e);
            }
        }

        private static EventMetadata CreateEventMetadata(bool isFolder, string entry, Exception e)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "ModifiedPathsDatabase");
            metadata.Add(nameof(entry), entry);
            metadata.Add(nameof(isFolder), isFolder);
            metadata.Add("Exception", e.ToString());
            return metadata;
        }

        private IEnumerable<string> GenerateDataLines()
        {
            foreach (string entry in this.modifiedPaths)
            {
                yield return this.FormatAddLine(entry);
            }
        }

        private void TraceWarning(bool isFolder, string entry, Exception e, string method)
        {
            if (this.Tracer != null)
            {
                EventMetadata metadata = CreateEventMetadata(isFolder, entry, e);
                this.Tracer.RelatedWarning(metadata, $"{e.GetType().Name} caught while processing {method}");
            }
        }

        private void TraceError(bool isFolder, string entry, Exception e, string method)
        {
            if (this.Tracer != null)
            {
                EventMetadata metadata = CreateEventMetadata(isFolder, entry, e);
                this.Tracer.RelatedError(metadata, $"{e.GetType().Name} caught while processing {method}");
            }
        }

        private bool TryParseAddLine(string line, out string key, out string value, out string error)
        {
            key = line;
            value = null;
            error = null;
            return true;
        }

        private bool TryParseRemoveLine(string line, out string key, out string error)
        {
            key = line;
            error = null;
            return true;
        }

        private string NormalizeEntryString(string virtualPath, bool isFolder)
        {
            // TODO(Mac) This can be optimized if needed
            return virtualPath.Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator).Trim(GVFSConstants.GitPathSeparator) +
                (isFolder ? GVFSConstants.GitPathSeparatorString : string.Empty);
        }
    }
}
