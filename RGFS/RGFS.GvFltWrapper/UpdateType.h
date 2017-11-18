#pragma once

namespace GvLib
{
    /// <summary>Enums to support update/delete operations on on-disk files in various states</summary>
    [System::FlagsAttribute]
    public enum class UpdateType : ULONG
    {
        /// <summary>
        /// 1. Allows the provider to Update/Delete a Placeholder 
        ///    or a Partial file irrespective of the state of the Metadata. 
        ///
        /// 2. The updates would be allowed only if its existing content ID 
        ///    does not match the new content ID provided by the provider.
        /// </summary>
        AllowDirtyMetadata = GV_UPDATE_ALLOW_DIRTY_METADATA,

        /// <summary>
        /// 1. Allows Gvflt to delete/update the file even if has dirty data 
        ///    (i.e. is a full file). Updates/Deletes to Placeholder 
        ///    or a Partial file in clean state are allowed too. The updates to 
        ///    Placeholder/Partial files  would be allowed only if its existing 
        ///    content ID does not match the new content ID provided by the Provider. 
        ///
        /// 2. Full file is directly converted to a placeholder with the Placeholder 
        ///    Info supplied by the Provider.
        /// </summary>
        AllowDirtyData = GV_UPDATE_ALLOW_DIRTY_DATA,

        /// <summary>
        /// 1. Allows Gvflt to update/delete the file even if it is a Tombstone. 
        ///    Updates/Deletes to Placeholder or a Partial file in clean state are 
        ///    allowed too. The updates to Placeholder/Partial files  would be 
        ///    allowed only if its existing content ID does not match the new content 
        ///    ID provided by the Provider.
        ///
        /// 2. For updates the tombstone would directly be converted to a 
        ///    placeholder with the Placeholder Info supplied by the provider.
        /// </summary>
        AllowTombstone = GV_UPDATE_ALLOW_TOMBSTONE,

        /// <summary>Allows update or delete of a file if it has the DOS read-only bit set.</summary>
        AllowReadOnly = GV_UPDATE_ALLOW_READ_ONLY
    };
}
