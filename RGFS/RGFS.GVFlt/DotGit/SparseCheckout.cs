using RGFS.Common;
using RGFS.Common.Git;
using RGFS.Common.Tracing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace RGFS.GVFlt.DotGit
{
    public class SparseCheckout
    {
        private FileSerializer sparseCheckoutSerializer;       

        // sparseCheckoutEntries
        // - Mirror of what’s on disk in the sparse-checkout file
        // - Files and folder paths in sparseCheckoutEntries should not be projected
        private ConcurrentHashSet<string> sparseCheckoutEntries;
        private RGFSContext context;

        public SparseCheckout(RGFSContext context, string virtualSparseCheckoutFilePath)
        {
            this.sparseCheckoutEntries = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.sparseCheckoutSerializer = new FileSerializer(context, virtualSparseCheckoutFilePath);
            this.context = context;            
        }
        
        public IEnumerable<string> Entries
        {
            get { return this.sparseCheckoutEntries; }
        }

        public int EntryCount
        {
            get { return this.sparseCheckoutEntries.Count; }
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
            string entry = this.NormalizeEntryString(virtualPath, isFolder: false);
            return this.AddEntry(entry, isFolder: false);
        }

        public CallbackResult AddFolderEntry(string virtualPath)
        {
            string entry = this.NormalizeEntryString(virtualPath, isFolder: true);
            return this.AddEntry(entry, isFolder: true);
        }

        public CallbackResult AddFileEntryFromIndex(string gitPath)
        {
            string entry = this.NormalizeEntryString(gitPath, isFolder: false);

            // Check Contains before calling AddEntry as Contains is lower weight than Add, and the vast 
            // majority of the time entries being added from the index will already be in the sparse-checkout file
            if (!this.sparseCheckoutEntries.Contains(entry))
            {
                return this.AddEntry(entry, isFolder: false);
            }

            return CallbackResult.Success;
        }
        
        private string NormalizeEntryString(string virtualPath, bool isFolder)
        {
            return RGFSConstants.GitPathSeparatorString +
                virtualPath.TrimStart(RGFSConstants.PathSeparator).Replace(RGFSConstants.PathSeparator, RGFSConstants.GitPathSeparator) +
                (isFolder ? RGFSConstants.GitPathSeparatorString : string.Empty);
        }

        private CallbackResult AddEntry(string normalizedEntry, bool isFolder)
        {            
            if (this.sparseCheckoutEntries.Add(normalizedEntry))
            {
                try
                {
                    this.sparseCheckoutSerializer.AppendLine(normalizedEntry);
                }
                catch (IOException e)
                {
                    CallbackResult result = CallbackResult.RetryableError;
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "SparseCheckout");
                    metadata.Add("normalizedEntry", normalizedEntry);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());                    
                    
                    // Remove the entry so that if AddRecursiveSparseCheckoutEntry is called again
                    // we'll try to append to the file again
                    if (!this.sparseCheckoutEntries.TryRemove(normalizedEntry))
                    {
                        result = CallbackResult.FatalError;
                        this.context.Tracer.RelatedError(metadata, "IOException caught while processing AddEntry, failed to undo addition to sparseCheckoutEntries");
                    }
                    else
                    {
                        this.context.Tracer.RelatedWarning(metadata, "IOException caught while processing AddEntry");
                    }                                      
                    
                    return result;
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "SparseCheckout");
                    metadata.Add("normalizedEntry", normalizedEntry);
                    metadata.Add("isFolder", isFolder);
                    metadata.Add("Exception", e.ToString());

                    this.context.Tracer.RelatedError(metadata, "Exception caught while processing AddEntry");
                    return CallbackResult.FatalError;
                }
            }

            return CallbackResult.Success;
        }
    }
}
