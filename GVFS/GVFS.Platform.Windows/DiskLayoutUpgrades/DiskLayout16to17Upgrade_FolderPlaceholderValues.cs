using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    /// <summary>
    /// Updates the values for folder placeholders from AllZeroSha to PlaceholderListDatabase.PartialFolderValue
    /// </summary>
    public class DiskLayout16to17Upgrade_FolderPlaceholderValues : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion => 16;

        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSRoot = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            try
            {
                string error;
                LegacyPlaceholderListDatabase placeholders;
                if (!LegacyPlaceholderListDatabase.TryCreate(
                    tracer,
                    Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.Databases.PlaceholderList),
                    new PhysicalFileSystem(),
                    out placeholders,
                    out error))
                {
                    tracer.RelatedError("Failed to open placeholder database: " + error);
                    return false;
                }

                using (placeholders)
                {
                    List<IPlaceholderData> oldPlaceholderEntries = placeholders.GetAllEntries();
                    List<IPlaceholderData> newPlaceholderEntries = new List<IPlaceholderData>();

                    foreach (IPlaceholderData entry in oldPlaceholderEntries)
                    {
                        if (entry.Sha == GVFSConstants.AllZeroSha)
                        {
                            newPlaceholderEntries.Add(new LegacyPlaceholderListDatabase.PlaceholderData(entry.Path, LegacyPlaceholderListDatabase.PartialFolderValue));
                        }
                        else
                        {
                            newPlaceholderEntries.Add(entry);
                        }
                    }

                    placeholders.WriteAllEntriesAndFlush(newPlaceholderEntries);
                }
            }
            catch (IOException ex)
            {
                tracer.RelatedError("Could not write to placeholder database: " + ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                tracer.RelatedError("Error updating placeholder database folder entries: " + ex.ToString());
                return false;
            }

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }
    }
}
