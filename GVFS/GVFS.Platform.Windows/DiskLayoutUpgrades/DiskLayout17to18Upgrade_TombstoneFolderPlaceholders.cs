using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout17to18Upgrade_TombstoneFolderPlaceholders : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion => 17;

        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            // Don't need to upgrade since the tombstone folders are only needed when a git command deletes folders
            // And the git command would have needed to be cancelled or crashed to leave tombstones that would need
            // to be tracked and persisted to the placeholder database.
            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }
    }
}
