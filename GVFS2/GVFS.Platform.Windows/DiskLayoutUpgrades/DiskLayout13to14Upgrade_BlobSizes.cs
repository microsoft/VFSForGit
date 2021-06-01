using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using GVFS.Virtualization.BlobSize;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout13to14Upgrade_BlobSizes : DiskLayoutUpgrade.MajorUpgrade
    {
        private static readonly string BlobSizesName = "BlobSizes";

        protected override int SourceMajorVersion
        {
            get { return 13; }
        }

        /// <summary>
        /// Version 13 to 14 added the (shared) SQLite blob sizes database
        /// </summary>
        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            string error;
            if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
            {
                tracer.RelatedError($"{nameof(DiskLayout13to14Upgrade_BlobSizes)}.{nameof(this.TryUpgrade)}: Could not initialize repo metadata: {error}");
                return false;
            }

            string newBlobSizesRoot;
            if (!this.TryFindNewBlobSizesRoot(tracer, enlistmentRoot, out newBlobSizesRoot))
            {
                return false;
            }

            this.MigrateBlobSizes(tracer, enlistmentRoot, newBlobSizesRoot);

            RepoMetadata.Instance.SetBlobSizesRoot(newBlobSizesRoot);
            tracer.RelatedInfo("Set BlobSizesRoot: " + newBlobSizesRoot);

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }

        private bool TryFindNewBlobSizesRoot(ITracer tracer, string enlistmentRoot, out string newBlobSizesRoot)
        {
            newBlobSizesRoot = null;

            string localCacheRoot;
            string error;
            if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
            {
                tracer.RelatedError($"{nameof(DiskLayout13to14Upgrade_BlobSizes)}.{nameof(this.TryFindNewBlobSizesRoot)}: Could not read local cache root from repo metadata: {error}");
                return false;
            }

            if (localCacheRoot == string.Empty)
            {
                // This is an old repo that was cloned prior to the shared cache
                // Blob sizes root should be <root>\.gvfs\databases\blobSizes
                newBlobSizesRoot = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.Name, GVFSEnlistment.BlobSizesCacheName);
            }
            else
            {
                // This repo was cloned with a shared cache, and the blob sizes should be a sibling to the git objects root
                string gitObjectsRoot;
                if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
                {
                    tracer.RelatedError($"{nameof(DiskLayout13to14Upgrade_BlobSizes)}.{nameof(this.TryFindNewBlobSizesRoot)}: Could not read git object root from repo metadata: {error}");
                    return false;
                }

                string cacheRepoFolder = Path.GetDirectoryName(gitObjectsRoot);
                newBlobSizesRoot = Path.Combine(cacheRepoFolder, GVFSEnlistment.BlobSizesCacheName);
            }

            return true;
        }

        private void MigrateBlobSizes(ITracer tracer, string enlistmentRoot, string newBlobSizesRoot)
        {
            string esentBlobSizeFolder = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, BlobSizesName);
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            if (!fileSystem.DirectoryExists(esentBlobSizeFolder))
            {
                tracer.RelatedInfo("Copied no ESENT blob size entries. {0} does not exist", esentBlobSizeFolder);
                return;
            }

            try
            {
                using (PersistentDictionary<string, long> oldBlobSizes = new PersistentDictionary<string, long>(esentBlobSizeFolder))
                using (BlobSizes newBlobSizes = new BlobSizes(newBlobSizesRoot, fileSystem, tracer))
                {
                    newBlobSizes.Initialize();

                    int copiedCount = 0;
                    int totalCount = oldBlobSizes.Count;
                    foreach (KeyValuePair<string, long> kvp in oldBlobSizes)
                    {
                        Sha1Id sha1;
                        string error;
                        if (Sha1Id.TryParse(kvp.Key, out sha1, out error))
                        {
                            newBlobSizes.AddSize(sha1, kvp.Value);

                            if (copiedCount++ % 5000 == 0)
                            {
                                tracer.RelatedInfo("Copied {0}/{1} ESENT blob size entries", copiedCount, totalCount);
                            }
                        }
                        else
                        {
                            tracer.RelatedWarning($"Corrupt entry ({kvp.Key}) found in BlobSizes, skipping.  Error: {error}");
                        }
                    }

                    newBlobSizes.Flush();
                    newBlobSizes.Shutdown();
                    tracer.RelatedInfo("Upgrade complete: Copied {0}/{1} ESENT blob size entries", copiedCount, totalCount);
                }
            }
            catch (EsentException ex)
            {
                tracer.RelatedWarning("BlobSizes appears to be from an older version of GVFS and corrupted, skipping upgrade of blob sizes: " + ex.Message);
            }
        }
    }
}
