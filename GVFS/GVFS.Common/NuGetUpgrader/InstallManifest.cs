using System.Collections.Generic;

namespace GVFS.Common
{
    public class InstallManifest
    {
        public string Version { get; set; }

        /// <summary>
        ///   Mapping of InstallManifest for a platform for different platform identifiers
        /// </summary>
        public Dictionary<string, InstallManifestPlatform> Platforms { get; set; }
    }
}
