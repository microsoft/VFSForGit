using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.NuGetUpgrade
{
    public class NuGetUpgradeOptions
    {
        public bool DryRun { get; set; }
        public bool NoVerify { get; set; }
    }
}
