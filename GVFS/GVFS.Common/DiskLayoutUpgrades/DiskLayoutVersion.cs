namespace GVFS.Common
{
    public class DiskLayoutVersion
    {
        public DiskLayoutVersion(int currentMajorVersion, int currentMinorVersion, int minimumSupportedMajorVersion)
        {
            this.CurrentMajorVersion = currentMajorVersion;
            this.CurrentMinorVersion = currentMinorVersion;
            this.MinimumSupportedMajorVersion = minimumSupportedMajorVersion;
        }

        // The major version should be bumped whenever there is an on-disk format change that requires a one-way upgrade.
        // Increasing this version will make older versions of GVFS unable to mount a repo that has been mounted by a newer
        // version of GVFS.
        public int CurrentMajorVersion { get; }

        // The minor version should be bumped whenever there is an upgrade that can be safely ignored by older versions of GVFS.
        // For example, this allows an upgrade step that sets a default value for some new config setting.
        public int CurrentMinorVersion { get; }

        // This is the last time GVFS made a breaking change that required a reclone. This should not
        // be incremented on platforms that have released a v1.0 as all their format changes should be
        // supported with an upgrade step.
        public int MinimumSupportedMajorVersion { get; }
    }
}
