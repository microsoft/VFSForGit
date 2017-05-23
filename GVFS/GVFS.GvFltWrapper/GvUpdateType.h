#pragma once

namespace GVFSGvFltWrapper
{
    [System::FlagsAttribute]
    public enum class GvUpdateType : unsigned long
    {
        UpdateAllowDirtyMetadata = GV_UPDATE_ALLOW_DIRTY_METADATA,
        UpdateAllowDirtyData = GV_UPDATE_ALLOW_DIRTY_DATA,
        UpdateAllowTombstone = GV_UPDATE_ALLOW_TOMBSTONE
    };
}
