using System;

namespace GVFS.Virtualization.FileSystem
{
    [Flags]
    public enum UpdateFailureReason : uint
    {
        // These values are identical to ProjFS.UpdateFailureCause to allow for easier casting
        NoFailure = 0,
        DirtyMetadata = 1,
        DirtyData = 2,
        Tombstone = 4,
        ReadOnly = 8
    }
}
