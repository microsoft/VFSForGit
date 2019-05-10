using GVFS.Common.DiskLayoutUpgrades;

namespace GVFS.Platform.Mac.DiskLayoutUpgrades
{
    public class DiskLayout18to19Upgrade_SqlitePlacholders : DiskLayoutUpgrade_SqlitePlaceholders
    {
        protected override int SourceMajorVersion => 18;
    }
}
