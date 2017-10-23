#pragma once

namespace GvLib
{
    [System::FlagsAttribute]
    public enum class UpdateType : unsigned long
    {
        AllowDirtyMetadata = GV_UPDATE_ALLOW_DIRTY_METADATA,
        AllowDirtyData = GV_UPDATE_ALLOW_DIRTY_DATA,
        AllowTombstone = GV_UPDATE_ALLOW_TOMBSTONE,
        AllowReadOnly = GV_UPDATE_ALLOW_READ_ONLY
    };
}
