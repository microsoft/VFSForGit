using GVFS.Common.Tracing;
using System.Collections.Generic;

namespace GVFS.DiskLayoutUpgrades
{
    public class DiskLayout12_0To12_1Upgrade : DiskLayoutUpgrade.MinorUpgrade
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
            string errorMessage;
            if (!this.TrySetGitConfig(
                tracer,
                enlistmentRoot,
                new Dictionary<string, string>
                {
                    { "status.aheadbehind", "false" },
                },
                out errorMessage))
            {
                return false;
            }

            return this.TryIncrementMinorVersion(tracer, enlistmentRoot);
        }
    }
}
