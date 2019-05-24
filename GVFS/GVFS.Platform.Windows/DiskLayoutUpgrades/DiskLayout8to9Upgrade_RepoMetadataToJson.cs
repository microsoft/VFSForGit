using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout8to9Upgrade_RepoMetadataToJson : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion
        {
            get { return 8; }
        }

        /// <summary>
        /// Rewrites ESENT RepoMetadata DB to flat JSON file
        /// </summary>
        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSRoot = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            if (!this.UpdateRepoMetadata(tracer, dotGVFSRoot))
            {
                return false;
            }

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }

        private bool UpdateRepoMetadata(ITracer tracer, string dotGVFSRoot)
        {
            string esentRepoMetadata = Path.Combine(dotGVFSRoot, WindowsDiskLayoutUpgradeData.EsentRepoMetadataName);
            if (Directory.Exists(esentRepoMetadata))
            {
                try
                {
                    using (PersistentDictionary<string, string> oldMetadata = new PersistentDictionary<string, string>(esentRepoMetadata))
                    {
                        string error;
                        if (!RepoMetadata.TryInitialize(tracer, dotGVFSRoot, out error))
                        {
                            tracer.RelatedError("Could not initialize RepoMetadata: " + error);
                            return false;
                        }

                        foreach (KeyValuePair<string, string> kvp in oldMetadata)
                        {
                            tracer.RelatedInfo("Copying ESENT entry: {0} = {1}", kvp.Key, kvp.Value);
                            RepoMetadata.Instance.SetEntry(kvp.Key, kvp.Value);
                        }
                    }
                }
                catch (IOException ex)
                {
                    tracer.RelatedError("Could not write to new repo metadata: " + ex.Message);
                    return false;
                }
                catch (EsentException ex)
                {
                    tracer.RelatedError("RepoMetadata appears to be from an older version of GVFS and corrupted: " + ex.Message);
                    return false;
                }

                string backupName;
                if (this.TryRenameFolderForDelete(tracer, esentRepoMetadata, out backupName))
                {
                    // If this fails, we leave behind cruft, but there's no harm because we renamed.
                    this.TryDeleteFolder(tracer, backupName);
                    return true;
                }
                else
                {
                    // To avoid double upgrading, we should rollback if we can't rename the old data
                    this.TryDeleteFile(tracer, RepoMetadata.Instance.DataFilePath);
                    return false;
                }
            }

            return true;
        }
    }
}