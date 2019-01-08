using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    public class InstallManifestPlatform
    {
        public InstallManifestPlatform()
        {
            this.InstallActions = new List<ManifestEntry>();
        }

        public InstallManifestPlatform(IEnumerable<ManifestEntry> entries)
        {
            this.InstallActions = entries?.ToList();
        }

        public List<ManifestEntry> InstallActions { get; set; }
    }
}
