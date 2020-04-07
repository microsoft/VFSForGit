using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization.BlobSize;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal class FolderData : FolderEntryData
        {
            public override bool IsFolder => true;

            public SortedFolderEntries ChildEntries { get; private set; }
            public bool ChildrenHaveSizes { get; private set; }
            public bool IsIncluded { get; set; } = true;

            public void ResetData(LazyUTF8String name, bool isIncluded)
            {
                this.Name = name;
                this.ChildrenHaveSizes = false;
                this.IsIncluded = isIncluded;
                if (this.ChildEntries == null)
                {
                    this.ChildEntries = new SortedFolderEntries();
                }

                this.ChildEntries.Clear();
            }

            public FileData AddChildFile(LazyUTF8String name, byte[] shaBytes)
            {
                return this.ChildEntries.AddFile(name, shaBytes);
            }

            public void Include()
            {
                this.IsIncluded = true;
                for (int i = 0; i < this.ChildEntries.Count; i++)
                {
                    if (this.ChildEntries[i].IsFolder)
                    {
                        FolderData folderData = (FolderData)this.ChildEntries[i];
                        folderData.Include();
                    }
                }
            }

            public string HashedFileShas()
            {
                byte[] sha = new byte[20];
                using (HashAlgorithm hash = SHA1.Create())
                {
                    for (int i = 0; i < this.ChildEntries.Count; i++)
                    {
                        if (!this.ChildEntries[i].IsFolder)
                        {
                            FileData fileData = (FileData)this.ChildEntries[i];
                            fileData.Sha.ToBuffer(sha);
                            hash.TransformBlock(sha, 0, 20, null, 0);
                        }
                    }

                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return SHA1Util.HexStringFromBytes(hash.Hash);
                }
            }

            public void PopulateSizes(
                ITracer tracer,
                GVFSGitObjects gitObjects,
                BlobSizes.BlobSizesConnection blobSizesConnection,
                Dictionary<string, long> availableSizes,
                CancellationToken cancellationToken)
            {
                if (this.ChildrenHaveSizes)
                {
                    return;
                }

                HashSet<string> missingShas;
                List<FileMissingSize> childrenMissingSizes;
                this.PopulateSizesLocally(tracer, gitObjects, blobSizesConnection, availableSizes, out missingShas, out childrenMissingSizes);

                lock (this)
                {
                    // Check ChildrenHaveSizes again in case another
                    // thread has already done the work of setting the sizes
                    if (this.ChildrenHaveSizes)
                    {
                        return;
                    }

                    this.PopulateSizesFromRemote(
                        tracer,
                        gitObjects,
                        blobSizesConnection,
                        missingShas,
                        childrenMissingSizes,
                        cancellationToken);
                }
            }

            /// <summary>
            /// Populates the sizes of child entries in the folder using locally available data
            /// </summary>
            private void PopulateSizesLocally(
                ITracer tracer,
                GVFSGitObjects gitObjects,
                BlobSizes.BlobSizesConnection blobSizesConnection,
                Dictionary<string, long> availableSizes,
                out HashSet<string> missingShas,
                out List<FileMissingSize> childrenMissingSizes)
            {
                if (this.ChildrenHaveSizes)
                {
                    missingShas = null;
                    childrenMissingSizes = null;
                    return;
                }

                missingShas = new HashSet<string>();
                childrenMissingSizes = new List<FileMissingSize>();
                for (int i = 0; i < this.ChildEntries.Count; i++)
                {
                    FileData childEntry = this.ChildEntries[i] as FileData;
                    if (childEntry != null)
                    {
                        string sha;
                        if (!childEntry.TryPopulateSizeLocally(tracer, gitObjects, blobSizesConnection, availableSizes, out sha))
                        {
                            childrenMissingSizes.Add(new FileMissingSize(childEntry, sha));
                            missingShas.Add(sha);
                        }
                    }
                }

                if (childrenMissingSizes.Count == 0)
                {
                    this.ChildrenHaveSizes = true;
                }
            }

            /// <summary>
            /// Populate sizes using size data from the remote
            /// </summary>
            /// <param name="missingShas">Set of object shas whose sizes should be downloaded from the remote.  This set should contains all the distinct SHAs from
            /// in childrenMissingSizes.  PopulateSizesLocally can be used to generate this set</param>
            /// <param name="childrenMissingSizes">List of child entries whose sizes should be downloaded from the remote.  PopulateSizesLocally
            /// can be used to generate this list</param>
            private void PopulateSizesFromRemote(
                ITracer tracer,
                GVFSGitObjects gitObjects,
                BlobSizes.BlobSizesConnection blobSizesConnection,
                HashSet<string> missingShas,
                List<FileMissingSize> childrenMissingSizes,
                CancellationToken cancellationToken)
            {
                if (childrenMissingSizes != null && childrenMissingSizes.Count > 0)
                {
                    Dictionary<string, long> objectLengths = gitObjects.GetFileSizes(missingShas, cancellationToken).ToDictionary(s => s.Id, s => s.Size, StringComparer.OrdinalIgnoreCase);
                    foreach (FileMissingSize childNeedingSize in childrenMissingSizes)
                    {
                        long blobLength = 0;
                        if (objectLengths.TryGetValue(childNeedingSize.Sha, out blobLength))
                        {
                            childNeedingSize.Data.Size = blobLength;
                            blobSizesConnection.BlobSizesDatabase.AddSize(
                                childNeedingSize.Data.Sha,
                                blobLength);
                        }
                        else
                        {
                            EventMetadata metadata = CreateEventMetadata();
                            metadata.Add("SHA", childNeedingSize.Sha);
                            tracer.RelatedError(metadata, "PopulateMissingSizesFromRemote: Failed to download size for child entry", Keywords.Network);
                            throw new SizesUnavailableException("Failed to download size for " + childNeedingSize.Sha);
                        }
                    }

                    blobSizesConnection.BlobSizesDatabase.Flush();
                }

                this.ChildrenHaveSizes = true;
            }

            // Wrapper for FileData that allows for caching string SHAs
            protected class FileMissingSize
            {
                public FileMissingSize(FileData fileData, string sha)
                {
                    this.Data = fileData;
                    this.Sha = sha;
                }

                public FileData Data { get; }

                public string Sha { get; }
            }
        }
    }
}
