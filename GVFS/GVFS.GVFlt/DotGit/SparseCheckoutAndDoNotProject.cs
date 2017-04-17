using GVFS.Common;
using GVFS.Common.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.IO;
using GVFS.Common.Git;

namespace GVFS.GVFlt.DotGit
{
    public class SparseCheckoutAndDoNotProject : IDisposable
    {
        private FileSerializer sparseCheckoutSerializer;       

        // sparseCheckoutEntries
        // - Mirror of what’s on disk in the sparse-checkout file
        // - Files and folder paths in sparseCheckoutEntries should not be projected
        private ConcurrentHashSet<string> sparseCheckoutEntries;

        // additionalDoNotProject
        // - File and folder paths that should not be projected, but git.exe does not need to know about (and
        //   so they are not in the sparse-checkout) 
        private PersistentDictionary<string, bool> additionalDoNotProject;
        private GVFSContext context;
        private GitIndexProjection gitIndexProjection;

        public SparseCheckoutAndDoNotProject(GVFSContext context, string virtualSparseCheckoutFilePath, string databaseName)
        {
            this.sparseCheckoutEntries = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.sparseCheckoutSerializer = new FileSerializer(context, virtualSparseCheckoutFilePath);

            this.additionalDoNotProject = new PersistentDictionary<string, bool>(
                Path.Combine(context.Enlistment.DotGVFSRoot, databaseName));
            this.context = context;            
        }

        public void LoadOrCreate(GitIndexProjection gitIndexProjection)
        {
            this.gitIndexProjection = gitIndexProjection;

            foreach (string line in this.sparseCheckoutSerializer.ReadAll())
            {
                string sanitizedFileLine;
                if (GitConfigHelper.TrySanitizeConfigFileLine(line, out sanitizedFileLine))
                {
                    this.sparseCheckoutEntries.Add(sanitizedFileLine);
                }
            }

            this.sparseCheckoutSerializer.Close();
        }

        public void Close()
        {
            this.sparseCheckoutSerializer.Close();
        }

        /// <summary>
        /// Checks if the specified path is in either the sparse-checkout file or the additionalDoNotProject
        /// database.  If the path is in neither of these, then it should be projected.
        /// </summary>
        /// <returns>True if the path should be projected, and false if it should not (i.e. because the
        /// path is in the sparse-checkout or additionalDoNotProject collections)</returns>
        public bool ShouldPathBeProjected(string virtualPath, bool isFolder)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder);
            return !(this.sparseCheckoutEntries.Contains(entry) || this.additionalDoNotProject.ContainsKey(entry));
        }

        public void StopProjecting(string virtualPath, bool isFolder)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder);
            if (!this.additionalDoNotProject.ContainsKey(entry))
            {
                // Use [] rather than Add to avoid ArgumentException if the key already exists.  
                this.additionalDoNotProject[entry] = true;
                this.additionalDoNotProject.Flush();
            }
        }

        public CallbackResult OnFolderCreated(string virtualPath)
        {
            return this.AddSparseCheckoutEntry(virtualPath, isFolder: true);
        }

        public CallbackResult OnFolderRenamed(string newVirtualPath)
        {
            return this.AddSparseCheckoutEntry(newVirtualPath, isFolder: true);
        }

        public CallbackResult OnFolderDeleted(string newVirtualPath)
        {
            return this.AddSparseCheckoutEntry(newVirtualPath, isFolder: true);
        }

        public CallbackResult OnPartialPlaceholderFolderCreated(string virtualPath)
        {
            this.StopProjecting(virtualPath, isFolder: true);
            return CallbackResult.Success;
        }

        public CallbackResult OnPlaceholderFileCreated(string virtualPath, DateTime createTimeUtc, DateTime lastWriteTimeUtc, long fileSize)
        {
            return this.AddFileEntryAndClearSkipWorktreeBit(virtualPath, createTimeUtc, lastWriteTimeUtc, fileSize);
        }

        public CallbackResult OnFileCreated(string virtualPath)
        {
            return this.AddFileEntryAndClearSkipWorktreeBit(virtualPath, createTimeUtc: DateTime.MinValue, lastWriteTimeUtc: DateTime.MinValue, fileSize: 0);
        }

        public CallbackResult OnFileRenamed(string virtualPath)
        {
            return this.AddFileEntryAndClearSkipWorktreeBit(virtualPath, createTimeUtc: DateTime.MinValue, lastWriteTimeUtc: DateTime.MinValue, fileSize: 0);
        }

        public bool HasEntryInSparseCheckout(string virtualPath, bool isFolder)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder);
            return this.sparseCheckoutEntries.Contains(entry);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.additionalDoNotProject != null)
                {
                    this.additionalDoNotProject.Dispose();
                    this.additionalDoNotProject = null;
                }
            }
        }

        private string NormalizeEntryString(string virtualPath, bool isFolder)
        {
            return GVFSConstants.GitPathSeparatorString +
                virtualPath.TrimStart(GVFSConstants.PathSeparator).Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator) +
                (isFolder ? GVFSConstants.GitPathSeparatorString : string.Empty);
        }

        private CallbackResult AddFileEntryAndClearSkipWorktreeBit(
            string virtualPath, 
            DateTime createTimeUtc, 
            DateTime lastWriteTimeUtc, 
            long fileSize)
        {
            string fileName = Path.GetFileName(virtualPath);
            CallbackResult result = this.AddSparseCheckoutEntry(virtualPath, isFolder: false);
            if (result != CallbackResult.Success)
            {
                return result;
            }

            return this.gitIndexProjection.ClearSkipWorktreeAndUpdateEntry(virtualPath, createTimeUtc, lastWriteTimeUtc, (uint)fileSize);
        }

        private CallbackResult AddSparseCheckoutEntry(string virtualPath, bool isFolder)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder);
            if (this.sparseCheckoutEntries.Add(entry))
            {
                try
                {
                    this.sparseCheckoutSerializer.AppendLine(entry);
                }
                catch (IOException e)
                {
                    CallbackResult result = CallbackResult.RetryableError;
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "SparseCheckoutAndDoNotProject");
                    metadata.Add("virtualFolderPath", virtualPath);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "IOException caught while processing AddSparseCheckoutEntry");
                    
                    // Remove the entry so that if AddRecursiveSparseCheckoutEntry is called again
                    // we'll try to append to the file again
                    if (!this.sparseCheckoutEntries.TryRemove(entry))
                    {
                        metadata["ErrorMessage"] += ", failed to undo addition to sparseCheckoutEntries";
                        result = CallbackResult.FatalError;
                    }
                                      
                    this.context.Tracer.RelatedError(metadata);
                    return result;
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "SparseCheckoutAndDoNotProject");
                    metadata.Add("virtualFolderPath", virtualPath);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "Exception caught while processing AddSparseCheckoutEntry");                    

                    // Remove the entry so that if AddRecursiveSparseCheckoutEntry is called again
                    // we'll try to append to the file again
                    if (!this.sparseCheckoutEntries.TryRemove(entry))
                    {
                        metadata["ErrorMessage"] += ", failed to undo addition to sparseCheckoutEntries";
                    }

                    this.context.Tracer.RelatedError(metadata);
                    return CallbackResult.FatalError;
                }
            }

            return CallbackResult.Success;
        }
    }
}
