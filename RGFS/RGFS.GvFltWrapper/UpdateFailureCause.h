#pragma once

namespace GvLib
{
    /// <summary> 
    /// Enums that are populated in the OutParam FailureReason in APIs
    /// DeleteFile and UpdatePlaceholderIfNeeded if a failure is returned
    /// owing to mismatch of File states and used UpdateFlags
    /// </summary> 
    [System::FlagsAttribute]
    public enum class UpdateFailureCause : unsigned long
    {
        /// <summary>
        /// This implies that an appropriate flag corresponding to the file state was set.
        /// If status is not NtStatus::Success it's owing to some other reason.
        /// </summary>
        NoFailure = GV_UPDATE_FAILURE_CAUSE_NO_FAILURE,

        /// <summary>
        /// If the flag AllowDirtyMetadata is not set in UpdateFlags,
        /// and the placeholder/partial file is found to have Dirty Metadata, this
        /// value is updated in FailureReason out parameter.
        /// </summary>
        DirtyMetadata = GV_UPDATE_FAILURE_CAUSE_DIRTY_METADATA,

        /// <summary>
        /// If the flag AllowDirtyData is not set in UpdateFlags,
        /// and the found file is a full file this
        /// value is updated in FailureReason out parameter.
        /// 
        /// It's noteworthy that a file with another reparse point or even symlinks and mountpoints
        /// are only possible if the provider specific reparse point has been
        /// removed.Hence for all purposes, these kind of files will be considered full
        /// files as well and will require necessary flags to be set.In case such files are encountered
        /// and AllowDirtyData is not set, this value will be returned used as
        /// Failure reason.
        /// </summary>
        DirtyData = GV_UPDATE_FAILURE_CAUSE_DIRTY_DATA,

        /// <summary>
        /// If the flag AllowTombstone is not set in UpdateFlags,
        /// and the found file is a Tombstone, this
        /// value is updated in FailureReason out parameter.
        /// </summary>
        Tombstone = GV_UPDATE_FAILURE_CAUSE_TOMBSTONE,

        /// <summary>
        /// If the flag AllowReadOnly is not set in UpdateFlags and deleting the file
        /// failed with NtStatus::CannotDelete.
        /// </summary>
        ReadOnly = GV_UPDATE_FAILURE_CAUSE_READ_ONLY
    };
}
