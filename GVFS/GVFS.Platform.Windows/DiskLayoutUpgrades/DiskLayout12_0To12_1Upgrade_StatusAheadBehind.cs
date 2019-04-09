using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using System.Collections.Generic;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout12_0To12_1Upgrade_StatusAheadBehind : DiskLayoutUpgrade.MinorUpgrade
    {
        protected override int SourceMajorVersion
        {
            get { return 12; }
        }

        protected override int SourceMinorVersion
        {
            get { return 0; }
        }

        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            if (!this.TrySetGitConfig(
                tracer,
                enlistmentRoot,
                new Dictionary<string, string>
                {
                    { "status.aheadbehind", "false" },
                }))
            {
                return false;
            }

            return this.TryIncrementMinorVersion(tracer, enlistmentRoot);
        }
    }
}
