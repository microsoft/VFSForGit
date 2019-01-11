using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.NuGetUpgrader
{
    public class InstallManifestPlatform
    {
        public InstallManifestPlatform()
        {
            this.InstallActions = new List<ManifestEntry>();
        }

        public InstallManifestPlatform(IEnumerable<ManifestEntry> entries)
        {
            this.InstallActions = entries?.ToList() ?? new List<ManifestEntry>();
        }

        public List<ManifestEntry> InstallActions { get; }
    }
}
