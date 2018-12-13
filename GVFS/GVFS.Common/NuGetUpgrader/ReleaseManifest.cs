using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public abstract class ReleaseManifest
    {
        public ReleaseManifest()
        {
            this.Properties = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            this.ManifestEntries = new List<ManifestEntry>();
        }

        public Dictionary<string, ManifestEntry> Properties { get; set; }
        public List<ManifestEntry> ManifestEntries { get; set; }

        public abstract void Read(string path);
    }
}
