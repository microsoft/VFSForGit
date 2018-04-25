using GVFS.Common.Tracing;

namespace GVFS.DiskLayoutUpgrades
{
    public class DiskLayout10to11Upgrade : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion
        {
            get { return 10; }
        }

        /// <summary>
        /// Version 10 to 11 only added a new value to BackgroundGitUpdate.OperationType,
        /// so we only need to bump the disk layout version version here.
        /// </summary>
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
