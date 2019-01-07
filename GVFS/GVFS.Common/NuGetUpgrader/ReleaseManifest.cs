using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// Details on the upgrade included in this package, including information
    /// on what packages are included and how to install them.
    /// </summary>
    public abstract class ReleaseManifest
    {
        public ReleaseManifest()
        {
            this.Entries = new List<ManifestEntry>();
        }

        /// <summary>
        /// The list of install actions.
        /// </summary>
        public List<ManifestEntry> Entries { get; private set; }

        /// <summary>
        /// Read the manifest from file.
        /// </summary>
        /// <param name="path"></param>
        public abstract void Read(string path);
    }
}
