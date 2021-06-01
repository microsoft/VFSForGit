using System;

namespace GVFS.Virtualization.FileSystem
{
    [Flags]
    public enum UpdatePlaceholderType : uint
    {
        // These values are identical to ProjFS.UpdateType to allow for easier casting
        AllowDirtyMetadata = 1,
        AllowDirtyData = 2,
        AllowTombstone = 4,
        AllowReadOnly = 32
    }
}
