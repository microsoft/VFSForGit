using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    /// <summary>
    /// This is a no-op upgrade step. It is here to prevent users from downgrading to a previous
    /// version of GVFS that is not GitStatusCache aware.
    ///
    /// This is because GVFS will set git config entries for the location of the git status cache when mounting,
    /// but does not unset them when unmounting (even if it did, it might not reliably unset these values).
    /// If a user downgrades, and they have a status cache file on disk, and git is configured to use the cache,
    /// then they might get stale / incorrect results after a downgrade. To avoid this possibility, we update
    /// the on-disk version during upgrade.
    /// </summary>
    public class DiskLayout15to16Upgrade_GitStatusCache : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion => 15;

        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }
    }
}
