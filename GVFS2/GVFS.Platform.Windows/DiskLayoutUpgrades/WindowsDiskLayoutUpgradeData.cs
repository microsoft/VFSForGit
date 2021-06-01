using GVFS.Common;
using GVFS.DiskLayoutUpgrades;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class WindowsDiskLayoutUpgradeData : IDiskLayoutUpgradeData
    {
        public const string DiskLayoutEsentVersionKey = "DiskLayoutVersion";
        public const string EsentRepoMetadataName = "RepoMetadata";

        public DiskLayoutUpgrade[] Upgrades
        {
            get
            {
                return new DiskLayoutUpgrade[]
                {
                    new DiskLayout7to8Upgrade_NewOperationType(),
                    new DiskLayout8to9Upgrade_RepoMetadataToJson(),
                    new DiskLayout9to10Upgrade_BackgroundAndPlaceholderListToFileBased(),
                    new DiskLayout10to11Upgrade_NewOperationType(),
                    new DiskLayout11to12Upgrade_SharedLocalCache(),
                    new DiskLayout12_0To12_1Upgrade_StatusAheadBehind(),
                    new DiskLayout12to13Upgrade_FolderPlaceholder(),
                    new DiskLayout13to14Upgrade_BlobSizes(),
                    new DiskLayout14to15Upgrade_ModifiedPaths(),
                    new DiskLayout15to16Upgrade_GitStatusCache(),
                    new DiskLayout16to17Upgrade_FolderPlaceholderValues(),
                    new DiskLayout17to18Upgrade_TombstoneFolderPlaceholders(),
                    new DiskLayout18to19Upgrade_SqlitePlacholders(),
                };
            }
        }

        public DiskLayoutVersion Version => new DiskLayoutVersion(
                    currentMajorVersion: 19,
                    currentMinorVersion: 0,
                    minimumSupportedMajorVersion: 7);

        public bool TryParseLegacyDiskLayoutVersion(string dotGVFSPath, out int majorVersion)
        {
            string repoMetadataPath = Path.Combine(dotGVFSPath, EsentRepoMetadataName);
            majorVersion = 0;
            if (Directory.Exists(repoMetadataPath))
            {
                try
                {
                    using (PersistentDictionary<string, string> oldMetadata = new PersistentDictionary<string, string>(repoMetadataPath))
                    {
                        string versionString = oldMetadata[DiskLayoutEsentVersionKey];
                        if (!int.TryParse(versionString, out majorVersion))
                        {
                            return false;
                        }
                    }
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            return true;
        }
    }
}
