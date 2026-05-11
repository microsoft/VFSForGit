using GVFS.Common;
using GVFS.DiskLayoutUpgrades;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class WindowsDiskLayoutUpgradeData : IDiskLayoutUpgradeData
    {
        public DiskLayoutUpgrade[] Upgrades
        {
            get
            {
                return new DiskLayoutUpgrade[]
                {
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
                    minimumSupportedMajorVersion: 14);

        public bool TryParseLegacyDiskLayoutVersion(string dotGVFSPath, out int majorVersion)
        {
            majorVersion = 0;
            return false;
        }
    }
}
