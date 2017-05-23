using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.IO;
using GVFS.Common.Git;

namespace GVFS.GVFlt.DotGit
{
    public class SparseCheckout
    {
        private FileSerializer sparseCheckoutSerializer;       

        // sparseCheckoutEntries
        // - Mirror of what’s on disk in the sparse-checkout file
        // - Files and folder paths in sparseCheckoutEntries should not be projected
        private ConcurrentHashSet<string> sparseCheckoutEntries;
        private GVFSContext context;

        public SparseCheckout(GVFSContext context, string virtualSparseCheckoutFilePath)
        {
            this.sparseCheckoutEntries = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.sparseCheckoutSerializer = new FileSerializer(context, virtualSparseCheckoutFilePath);
            this.context = context;            
        }

        public int EntryCount
        {
            get
            {
                return this.sparseCheckoutEntries.Count;
            }
        }

        public void LoadOrCreate()
        {
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

        public bool HasEntry(string virtualPath, bool isFolder)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder);
            return this.sparseCheckoutEntries.Contains(entry);
        }

        public CallbackResult AddFileEntry(string virtualPath)
        {
            return this.AddEntry(virtualPath, isFolder: false);
        }

        public CallbackResult AddFolderEntry(string virtualPath)
        {
            return this.AddEntry(virtualPath, isFolder: true);
        }

        private string NormalizeEntryString(string virtualPath, bool isFolder)
        {
            return GVFSConstants.GitPathSeparatorString +
                virtualPath.TrimStart(GVFSConstants.PathSeparator).Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator) +
                (isFolder ? GVFSConstants.GitPathSeparatorString : string.Empty);
        }

        private CallbackResult AddEntry(string virtualPath, bool isFolder)
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
                    metadata.Add("Area", "SparseCheckout");
                    metadata.Add("virtualFolderPath", virtualPath);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "IOException caught while processing AddEntry");
                    
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
                    metadata.Add("Area", "SparseCheckout");
                    metadata.Add("virtualFolderPath", virtualPath);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "Exception caught while processing AddEntry");                    

                    this.context.Tracer.RelatedError(metadata);
                    return CallbackResult.FatalError;
                }
            }

            return CallbackResult.Success;
        }
    }
}
