using System.Collections.Generic;

namespace GVFS.Common
{
    public class InstallManifest
    {
        public string Version { get; set; }

        public Dictionary<string, InstallManifestPlatform> Platforms { get; set; }
    }
}
