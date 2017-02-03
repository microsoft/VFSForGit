using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using GVFSGvFltWrapper;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.GVFlt
{
    public class GVFltFolder
    {
        private List<GVFltFileInfo> entries;

        public GVFltFolder(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            DotGit.SparseCheckoutAndDoNotProject sparseCheckoutAndDoNotProject,
            PersistentDictionary<string, long> blobSizes,
            string virtualPath,
            string projectedCommitId)
        {
            List<GitTreeEntry> treeEntries = context.Repository.GetTreeEntries(projectedCommitId, virtualPath).ToList();
            treeEntries = GetProjectedEntries(context, sparseCheckoutAndDoNotProject, virtualPath, treeEntries);
            this.PopulateNamedEntrySizes(gitObjects, blobSizes, virtualPath, context, treeEntries);

            this.entries = new List<GVFltFileInfo>();
            foreach (GitTreeEntry entry in treeEntries)
            {
                this.entries.Add(new GVFltFileInfo(entry.Name, entry.IsBlob ? entry.Size : 0, entry.IsTree));
            }

            this.entries.Sort(GVFltFileInfo.SortAlphabeticallyIgnoreCase());
        }

        public IEnumerable<GVFltFileInfo> GetItems()
        {
            return this.entries;
        }

        public GVFltFileInfo GetFileInfo(string name)
        {
            return this.entries.Find(fileInfo => fileInfo.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static List<GitTreeEntry> GetProjectedEntries(GVFSContext context, DotGit.SparseCheckoutAndDoNotProject sparseCheckoutAndDoNotProject, string virtualPath, List<GitTreeEntry> treeEntries)
        {
            List<GitTreeEntry> projectedTreeEntries = new List<GitTreeEntry>();
            foreach (GitTreeEntry entry in treeEntries)
            {
                string entryVirtualPath = Path.Combine(virtualPath, entry.Name);
                if (sparseCheckoutAndDoNotProject.ShouldPathBeProjected(entryVirtualPath, entry.IsTree))
                {
                    projectedTreeEntries.Add(entry);
                }
            }

            return projectedTreeEntries;
        }

        private void PopulateNamedEntrySizes(
            GVFSGitObjects gitObjects,
            PersistentDictionary<string, long> blobSizes,
            string parentVirtualPath,
            GVFSContext context,
            IEnumerable<GitTreeEntry> entries)
        {
            List<GitTreeEntry> blobs = entries.Where(e => e.IsBlob).ToList();

            // Then try to find as many blob sizes locally as possible.  
            List<GitTreeEntry> entriesMissingSizes = new List<GitTreeEntry>();
            foreach (GitTreeEntry namedEntry in blobs.Where(b => b.Size == 0))
            {
                long blobLength = 0;
                if (blobSizes.TryGetValue(namedEntry.Sha, out blobLength))
                {
                    namedEntry.Size = blobLength;
                }
                else if (gitObjects.TryGetBlobSizeLocally(namedEntry.Sha, out blobLength))
                {
                    namedEntry.Size = blobLength;
                    blobSizes[namedEntry.Sha] = blobLength;
                }
                else
                {
                    entriesMissingSizes.Add(namedEntry);
                }
            }

            // Anything remaining should come from the remote.
            if (entriesMissingSizes.Count > 0)
            {
                Dictionary<string, long> objectLengths = gitObjects.GetFileSizes(entriesMissingSizes.Select(e => e.Sha).Distinct()).ToDictionary(s => s.Id, s => s.Size, StringComparer.OrdinalIgnoreCase);
                foreach (GitTreeEntry namedEntry in entriesMissingSizes)
                {
                    long blobLength = 0;
                    if (objectLengths.TryGetValue(namedEntry.Sha, out blobLength))
                    {
                        namedEntry.Size = blobLength;
                        blobSizes[namedEntry.Sha] = blobLength;
                    }
                    else
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("ErrorMessage", "GvFltException: Failed to download size for: " + namedEntry.Name + ", SHA: " + namedEntry.Sha);
                        context.Tracer.RelatedError(metadata, Keywords.Network);
                        throw new GvFltException(StatusCode.StatusFileNotAvailable);
                    }
                }
            }
        }
    }
}
