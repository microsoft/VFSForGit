#pragma once

#include "CallbackDelegates.h"
#include "UpdateFailureCause.h"
#include "HResult.h"
#include "NtStatus.h"
#include "WriteBuffer.h"

namespace GvLib
{
    /// <summary>Interface to allow for easier unit testing </summary>
    public interface class IVirtualizationInstance
    {
    public:

        /// <summary>Start directory enumeration callback</summary>
        /// <seealso cref="StartDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property StartDirectoryEnumerationEvent^ OnStartDirectoryEnumeration
        {
            StartDirectoryEnumerationEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(StartDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>End directory enumeration callback</summary>
        /// <seealso cref="EndDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property EndDirectoryEnumerationEvent^ OnEndDirectoryEnumeration
        {
            EndDirectoryEnumerationEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(EndDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>Get next enumeration result callback</summary>
        /// <seealso cref="GetDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetDirectoryEnumerationEvent^ OnGetDirectoryEnumeration
        {
            GetDirectoryEnumerationEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(GetDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>Query file name callback</summary>
        /// <seealso cref="QueryFileNameEvent"/>
        /// <remarks>This callback is required</remarks>
        property QueryFileNameEvent^ OnQueryFileName
        {
            QueryFileNameEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(QueryFileNameEvent^ eventCB);
        }

        /// <summary>Get placeholder callback</summary>
        /// <seealso cref="GetPlaceholderInformationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetPlaceholderInformationEvent^ OnGetPlaceholderInformation
        {
            GetPlaceholderInformationEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(GetPlaceholderInformationEvent^ eventCB);
        };

        /// <summary>Get file stream callback</summary>
        /// <seealso cref="GetPlaceholderInformationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetFileStreamEvent^ OnGetFileStream
        {
            GetFileStreamEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(GetFileStreamEvent^ eventCB);
        };

        /// <summary>First write notification callback</summary>
        /// <seealso cref="NotifyFirstWriteEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFirstWriteEvent^ OnNotifyFirstWrite
        {
            NotifyFirstWriteEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyFirstWriteEvent^ eventCB);
        };

        /// <summary>
        /// File handle created notification callback (when IoStatusBlockValue is not FileSuperseded, FileOverwritten, or FileCreated)
        /// </summary>
        /// <seealso cref="NotifyPostCreateHandleOnlyEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPostCreateHandleOnlyEvent^ OnNotifyPostCreateHandleOnly
        {
            NotifyPostCreateHandleOnlyEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPostCreateHandleOnlyEvent^ eventCB);
        };

        /// <summary>File handle created notification callback (when a new file or folder has been created)</summary>
        /// <seealso cref="NotifyPostCreateNewFileEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPostCreateNewFileEvent^ OnNotifyPostCreateNewFile
        {
            NotifyPostCreateNewFileEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPostCreateNewFileEvent^ eventCB);
        };

        /// <summary>File handle created notification callback (when the IoStatusBlockValue is FileOverwritten or FileSuperseded)</summary>
        /// <seealso cref="NotifyPostCreateOverwrittenOrSupersededEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPostCreateOverwrittenOrSupersededEvent^ OnNotifyPostCreateOverwrittenOrSuperseded
        {
            NotifyPostCreateOverwrittenOrSupersededEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPostCreateOverwrittenOrSupersededEvent^ eventCB);
        };

        /// <summary>Pre-delete notification callback</summary>
        /// <seealso cref="NotifyPreDeleteEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreDeleteEvent^ OnNotifyPreDelete
        {
            NotifyPreDeleteEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPreDeleteEvent^ eventCB);
        }

        /// <summary>Pre-rename notification callback</summary>
        /// <seealso cref="NotifyPreRenameEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreRenameEvent^ OnNotifyPreRename
        {
            NotifyPreRenameEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPreRenameEvent^ eventCB);
        }

        /// <summary>Pre-set-hardlink notification callback</summary>
        /// <seealso cref="NotifyPreSetHardlinkEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreSetHardlinkEvent^ OnNotifyPreSetHardlink
        {
            NotifyPreSetHardlinkEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyPreSetHardlinkEvent^ eventCB);
        }

        /// <summary>File renamed notification callback</summary>
        /// <seealso cref="NotifyFileRenamedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileRenamedEvent^ OnNotifyFileRenamed
        {
            NotifyFileRenamedEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyFileRenamedEvent^ eventCB);
        }

        /// <summary>Hardlink created notification callback</summary>
        /// <seealso cref="NotifyHardlinkCreatedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyHardlinkCreatedEvent^ OnNotifyHardlinkCreated
        {
            NotifyHardlinkCreatedEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyHardlinkCreatedEvent^ eventCB);
        }

        /// <summary>
        /// File handle closed notification callback (when handle was not used to modify or delete file)
        /// </summary>
        /// <seealso cref="NotifyFileHandleClosedOnlyEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileHandleClosedOnlyEvent^ OnNotifyFileHandleClosedOnly
        {
            NotifyFileHandleClosedOnlyEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyFileHandleClosedOnlyEvent^ eventCB);
        }

        /// <summary>File handle closed notification callback (when handle was used to modify and\or delete file)</summary>
        /// <seealso cref="NotifyFileHandleClosedModifiedOrDeletedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileHandleClosedModifiedOrDeletedEvent^ OnNotifyFileHandleClosedModifiedOrDeleted
        {
            NotifyFileHandleClosedModifiedOrDeletedEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(NotifyFileHandleClosedModifiedOrDeletedEvent^ eventCB);
        }

        /// <summary>Command cancelled callback</summary>
        /// <seealso cref="CancelCommandEvent"/>
        /// <remarks>This callback is optional</remarks>
        property CancelCommandEvent^ OnCancelCommand
        {
            CancelCommandEvent^ get(void);

            /// <exception cref="System::InvalidOperationException">
            /// Thrown if the VirtualizationInstance has already been started
            /// </exception>
            void set(CancelCommandEvent^ eventCB);
        }

        /// <summary>Starts a GvFlt virtualization instance</summary>
        /// <param name="virtualizationRootPath">
        /// The path to the virtualization root directory.  This directory must have already been
        /// converted to a virtualization root using ConvertDirectoryToVirtualizationRoot.
        /// </param>
        /// <param name="poolThreadCount">
        /// The number of threads to wait on the completion port and process commands.
        /// The PoolThreadCount has to > 4 otherwise an invaid parameter error will be returned.
        /// </param>
        /// <param name="concurrentThreadCount">
        /// The target maximum number of threads to run concurrently.  
        /// The actual number of threads can be less than this (if no commands are waiting for threads)
        /// or more than this (if one or more threads become computable after waits complete).
        /// See also - https://msdn.microsoft.com/en-us/library/windows/desktop/aa363862(v=vs.85).aspx
        /// </param>
        /// <param name="enableNegativePathCache">
        /// If true, when the provider returns ObjectNameNotFound
        /// for a path from OnGetPlaceholderInformation callback, GvFlt will remember 
        /// that this path doesn't exist in the provider's namespace, and fail 
        /// the subsequent file opens for the same path without consulting the provider.
        /// The provider can call ClearNegativePathCache to clear this cache.
        /// </param>
        /// <param name="globalNotificationMask">
        /// Bit mask that indicates which notifications the provider wants for each file under the virtualization root.
        /// </param>
        /// <param name ="logicalBytesPerSector">
        /// [Out] Logical bytes per sector reported by physical storage.  Used by CreateWriteBuffer to determine size
        /// of write buffer.
        /// </param>
        /// <param name ="writeBufferAlignment">
        /// [Out] Memory alignment that will be used when CreateWriteBuffer creates write buffers.
        /// </param>
        /// <returns>
        /// If StartVirtualizationInstance succeeds, Success is returned.
        ///
        /// If GvFlt filter driver is not loaded, PrivilegeNotHeld is returned.  
        ///
        /// If StartVirtualizationInstance fails, the appropriate error is returned.
        /// </returns>
        /// <remarks>
        /// Currently only one VirtualizationInstance can be running at a time.
        ///
        /// StartVirtualizationInstance function starts a GvFlt virtualization instance by performing below actions:
        /// 
        ///     1) Attaches the GvFlt driver to the volume that contains the virtualization root
        ///     2) Establishes two comm ports to the driver
        ///     3) Registers the callback routine that handles commands from GvFlt
        /// 
        /// If GvFlt filter is already attached to the volume, this function can be called from a non-elevated process.
        /// Otherwise this function will attempt to attach the filter to the volume which requires admin privilege,
        /// access denied error will be returned if called from a non-elevated process.
        /// </remarks>
        /// <exception cref="System::ArgumentNullException"/>
        /// <exception cref="System::InvalidOperationException">
        /// Thrown if there is already another running VirtualizationInstance
        /// </exception>
        /// <exception cref="GvLibException">
        /// Thrown if there is a failure determining logicalBytesPerSector or writeBufferAlignment
        /// </exception> 
        HResult StartVirtualizationInstance(
            System::String^ virtualizationRootPath,
            unsigned long poolThreadCount,
            unsigned long concurrentThreadCount,
            bool enableNegativePathCache,
            NotificationType globalNotificationMask,
            unsigned long% logicalBytesPerSector,
            unsigned long% writeBufferAlignment);

        /// <summary>Stops the virtualization instance</summary>
        /// <returns>If StopVirtualizationInstance succeeds, Success is returned.  If StartVirtualizationInstance fails, the appropriate error is returned.</returns>
        HResult StopVirtualizationInstance();

        /// <summary>Detaches GvFlt driver from the volume</summary>
        /// <returns>If DetachDriver succeeds, Success is returned, otherwise the appropriate error is returned.</returns>
        /// <remarks>
        /// For this call to succeed, there must not be any active virtualization instance on the volume.
        /// All provider processes need to call StopVirtualizationInstance for their active instances first.
        /// </remarks>
        HResult DetachDriver();

        /// <summary>Clears the negative path cache for this VirtualizationInstance</summary>
        /// <param name="totalEntryNumber">
        /// Output parameter that stores the total number of entries that were in the negative path cache when it was cleared.
        /// </param>
        /// <returns>If ClearNegativePathCache succeeds, Success is returned, otherwise the appropriate error is returned.</returns>
        /// <remarks>
        /// For this call to succeed, there must not be any active virtualization instance on the volume.
        /// All provider processes need to call StopVirtualizationInstance for their active instances first.
        /// </remarks>
        NtStatus ClearNegativePathCache(unsigned long% totalEntryNumber);

        /// <summary>Send file stream data to the GvFlt driver in a OnGetFileStream callback</summary>
        /// <param name="streamGuid">
        /// The Guid to associate with this file stream for a set of WriteFile commands.
        /// The provider mush pass in this Guid when calling WriteFile in the callback for the same file stream.
        /// </param>
        /// <param name="buffer">
        /// Buffer containing the file stream data.  This buffer contains only the file stream data specified 
        /// by byteOffset and length — no data headers are included.  The buffer must be at least “length” bytes in size
        /// </param>
        /// <param name="byteOffset">
        /// The offset from the beginning of the file data stream in question to where the data 
        /// contained in buffer should be written
        /// </param>
        /// <param name="length">The number of bytes of data that should be written to the file from the supplied buffer</param>
        /// <returns>
        /// If WriteFile succeeds, Success is returned.
        ///
        /// If buffer is nullptr or Length is 0, InvalidParameter is returned.
        ///
        /// If WriteFile or GvFlt filter fails to allocate memory, InsufficientResources is returned.
        ///
        /// If WriteFile fails to send the message to GvFlt filter, InternalError is returned.
        ///    see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
        ///
        /// If GvFlt responses the message with a unexpected message type, InternalError is returned.
        ///
        /// If GvFlt filter fails to write to the file, the NtStatus error from file system will be returned.
        ///    see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff544610(v=vs.85).aspx
        /// </returns>
        /// <remarks>OnGetFileStream callback must not return until it completes sending back data with WriteFile</remarks>
        NtStatus WriteFile(
            System::Guid streamGuid,
            WriteBuffer^ buffer,
            unsigned long long byteOffset,
            unsigned long length);

        /// <summary>Deletes an on-disk file without raising notification callbacks</summary>
        /// <param name="relativePath">The path (relative to the virtualization root) of the file to delete</param>
        /// <param name="updateFlags">Any combination of flags from UpdateType</param>
        /// <param name="failureReason">
        /// [Out] If there is a failure owing to mismatch between file state and the updateFlags used, 
        /// this contains the reason for the mismatch.
        /// </param>
        /// <returns>
        /// If DeleteFile succeeds, Success is returned.
        /// 
        /// If DestinationFileName is NULL or Length is 0, InvalidParameter is returned.
        /// 
        /// If PlaceholderInformation is NULL, InvalidParameter is returned.
        /// 
        /// If the file is a dirty "Placeholder" or a "Partial" file and the flag
        ///     AllowDirtyMetadata is not one of the set flags, FileSystemVirtualizationInvalidOperation will
        ///     be returned.  FailureReason will indicate the reason as DirtyMetadata.
        /// 
        /// If the file is a "Full" file and flag AllowDirtyData is not one
        ///     of the set flags FileSystemVirtualizationInvalidOperation
        ///     will be returned.  FailureReason will indicate the reason as DirtyData.
        /// 
        /// If the file is a "Tombstone" and flag AllowTombstone is not one of the set flags 
        ///     FileSystemVirtualizationInvalidOperation will be returned.  FailureReason will indicate the reason as Tombstone
        /// 
        /// For all allowed updateFlags values, if the file is a “Virtual File”, GvFlt will not be able to open a handle to it
        ///     and hence the file system error ObjectNameNotFound would be returned.
        /// 
        /// If the function or GvFlt filter fails to allocate memory, InsufficientResources is returned.
        /// 
        /// If the function fails to send the message to GvFlt filter, InternalError is returned.
        ///     See also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
        /// 
        /// If GvFlt responds to the message with a unexpected message type, InternalError is returned.
        /// 
        /// If GvFlt cannot open the file, the NtStatus error from the file system will be returned.
        /// 
        /// For any delete related operation, NtStatus error from the file system will be returned.
        /// </returns>
        /// <remarks>
        /// Based on the value of updateFlags, GvFlt behaves in the following manner :
        /// For a file in state :
        /// 
        /// 1. Placeholder / Partial:
        ///     If metadata is not dirty, GvFlt will always attempt deleting the file
        ///     irrespective of the flags set in UpdateFlags.
        /// 
        ///     If the metadata is dirty, the file will only be deleted if the one of the
        ///     bits set in updateFlags is AllowDirtyMetadata.
        /// 
        /// 2. Full:
        ///     This file will be deleted if the one of the bits set in UpdateFlags is AllowDirtyData
        /// 
        /// 3. Tombstone:
        ///     This file will be deleted if one of the bits set in UpdateFlags is AllowTombstone.
        /// 
        ///     Based on the sets of bits set, GvFlt will provide the functionality.  The provider
        ///     can use a combination based on the desired behavior.
        /// 
        /// Note: For a directory, the only valid states are Virtual, Placeholder / Partial with
        ///       clean and dirty metadata.  Hence the flag AllowDirtyMetadata would suffice for a directory.
        /// </remarks>
        NtStatus DeleteFile(
            System::String^ relativePath,
            UpdateType updateFlags,
            UpdateFailureCause% failureReason);

        /// <summary>Sends file or directory metadata to the GvFlt driver in a GetPlaceholderInformation callback</summary>
        /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
        /// <param name="creationTime">Creation time</param>
        /// <param name="lastAccessTime">Last access time</param>
        /// <param name="lastWriteTime">Last write time</param>
        /// <param name="changeTime">Change time</param>
        /// <param name="fileAttributes">File attributes</param>
        /// <param name="endOfFile">File length</param>
        /// <param name="directory">True if relativePath is a folder, false if relativePath is a file</param>
        /// <param name="contentId">ContentId to store in placeholder, can be nullptr</param>
        /// <param name="epochId">EpochId to store in placeholder, can be nullptr</param>
        /// <returns>    
        /// If WritePlaceholderInformation succeeds, Success is returned.
        /// 
        /// If relativePath is nullptr, InvalidParameter is returned.
        ///
        /// If WritePlaceholderInformation or GvFlt filter fails to allocate memory, InsufficientResources is returned.
        /// 
        /// If WritePlaceholderInformation fails to send the message to GvFlt filter, InternalError is returned.
        ///     see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
        /// 
        /// If GvFlt responds to the message with a unexpected message type, InternalError is returned.
        /// 
        /// If GvFlt filter fails to create the new file, the NtStatus error from file system will be returned.
        ///     see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541939(v=vs.85).aspx
        ///</returns>
        /// <remarks>
        /// contentId and epochId have a maximum length of PlaceholderIdLength.  Any data beyond 
        /// PlaceholderIdLength will be ignored.
        /// </remarks>
        NtStatus WritePlaceholderInformation(
            System::String^ relativePath,
            System::DateTime creationTime,
            System::DateTime lastAccessTime,
            System::DateTime lastWriteTime,
            System::DateTime changeTime,
            unsigned long fileAttributes,
            long long endOfFile,
            bool directory,
            array<System::Byte>^ contentId,
            array<System::Byte>^ epochId);

        /// <summary>Create a hardlink in lieu of creating a placeholder</summary>
        /// <param name="destinationFileName">The path (relative to the virtualization root) of the file</param>
        /// <param name="hardLinkTarget">
        /// The full path to the existing file that the placeholder hard link will link to.
        /// This path must be on the same volume as the virtualization instance.
        /// </param>
        /// <returns>    
        /// If CreatePlaceholderAsHardlink succeeds, Success is returned.
        /// 
        /// If destinationFileName or hardLinkTarget is nullptr or empty string, InvalidParameter is returned.
        ///
        /// If CreatePlaceholderAsHardlink or GvFlt filter fails to allocate memory, InsufficientResources is returned.
        /// 
        /// If CreatePlaceholderAsHardlink fails to send the message to GvFlt filter, InternalError is returned.
        ///     see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
        /// 
        /// If GvFlt responds to the message with a unexpected message type, InternalError is returned.
        /// 
        /// If GvFlt filter fails to create the hard link, the NtStatus error from file system will be returned.
        ///     see also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541939(v=vs.85).aspx
        ///</returns>
        /// <remarks>
        /// The caller uses CreatePlaceholderAsHardlink to indicate that the placeholder
        /// should be a hard link to an already - existing file, instead of a proper placeholder.
        /// </remarks>
        NtStatus CreatePlaceholderAsHardlink(
            System::String^ destinationFileName,
            System::String^ hardLinkTarget);

        /// <summary>Update placeholder information for a file</summary>
        /// <param name="relativePath">The path (relative to the virtualization root) of the file</param>
        /// <param name="creationTime">Creation time (to set in placeholder)</param>
        /// <param name="lastAccessTime">Last access time (to set in placeholder)</param>
        /// <param name="lastWriteTime">Last write time (to set in placeholder)</param>
        /// <param name="changeTime">Change time (to set in placeholder)</param>
        /// <param name="fileAttributes">File attributes (to set in placeholder)</param>
        /// <param name="endOfFile">File length (to set in placeholder)</param>
        /// <param name="contentId">ContentId  (to set in placeholder), can be nullptr</param>
        /// <param name="epochId">EpochId  (to set in placeholder), can be nullptr</param>
        /// <param name="updateFlags">Any combination of flags from UpdateType</param>
        /// <param name="failureReason">
        /// [Out] If there is a failure owing to mismatch between file state and the updateFlags used, 
        /// this contains the reason for the mismatch.
        /// </param>
        /// <returns>
        /// If UpdatePlaceholderIfNeeded succeeds Success is returned.  Note that even when no update is required to a
        /// Partial or a Placeholder file when its existing content ID matches to the new content ID provided
        /// by the provider, Success is returned.
        /// 
        /// If DestinationFileName is NULL or Length is 0, InvalidParameter is returned.
        /// 
        /// If the file is a dirty "Placeholder" or a "Partial" file and the flag AllowDirtyMetadata is not one of
        ///     the set flags, FileSystemVirtualizationInvalidOperation will be returned.  FailureReason
        ///      will indicate the reason as DirtyMetadata.
        /// 
        /// If the file is a "Full" file and flag AllowDirtyData is not one of the set flags 
        ///     FileSystemVirtualizationInvalidOperation will be returned.  FailureReason will 
        ///     indicate the reason as DirtyData.
        /// 
        /// If the file is a "Tombstone" and flag AllowTombstone is not one
        ///     of the set flags FileSystemVirtualizationInvalidOperation will
        ///     be returned.  FailureReason will indicate the reason as Tombstone.
        /// 
        /// For all allowed UpdateFlags values, if the file is a “Virtual File”, GvFlt will not be able to open
        /// a handle to it and hence the file system error ObjectNameNotFound would be returned.
        /// 
        /// If the function or GvFlt filter fails to allocate memory, InsufficientResources is returned.
        /// 
        /// If the function fails to send the message to GvFlt filter, InternalError is returned.
        /// See also - https://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
        /// 
        /// If GvFlt responds to the message with a unexpected message type, InternalError is returned.
        /// 
        /// If GvFlt cannot open the file, the NtStatus error from the file system will be returned.
        /// 
        /// For all UpdateFlags values the following apply as we follow a Create New, Rename new to old
        /// (replace_if_existing set to true) approach:
        ///    - During the delete operation, errors received from the Filesystem would apply.
        ///    - During creating a new placeholder, all errors in WritePlaceholderInformation apply.
        /// 
        /// </returns>
        /// <remarks>
        /// Based on the value of the flags, GvFlt would behave in the following manner-
        /// 
        /// For a file in state :
        /// 
        /// 1. Placeholder / Partial :
        ///     If metadata is not dirty, GvFlt will always attempt Updating the file
        ///     irrespective of the flags set in updateFlags.
        /// 
        ///     If the metadata is dirty, the file will only be updated if the one of the
        ///     bits set in UpdateFlags is AllowDirtyMetadata.
        /// 
        ///     If the conditions above are satisfied Updates to Partial / Placeholder files
        ///     will only be allowed if its existing contentIddoes not match the new
        ///     contentId provided by the provider
        /// 
        /// 2. Full:
        ///    This file will be updated if the one of the bits set in UpdateFlags is
        ///    AllowDirtyData.  Full File is directly converted to a placeholder
        ///    with the placeholder values supplied by the Provider.
        /// 
        /// 3. Tombstone :
        ///     This file will be Updated if one of the bits set in UpdateFlags is
        ///     AllowTombstone.  A tombstone is directly converted to a placeholder
        ///     with the placeholder values supplied by the Provider.
        /// 
        /// Based on the sets of bits set, GvFlt will provide the functionality.  The provider
        /// can use a combination based on the desired behavior.
        /// 
        /// Note: For a directory, the only valid states are Virtual, Placeholder / Partial with
        /// clean and dirty metadata.Hence the flag AllowDirtyMetadata would suffice for a directory.
        ///         
        /// When no flag is provided or UpdateFlags is set to 0, it can be used for updating a “Placeholder”
        /// or a “Partial” file which is in a clean state – i.e. even its metadata is not dirty, with the
        /// placeholder information supplied by the provider
        /// </remarks>
        NtStatus UpdatePlaceholderIfNeeded(
            System::String^ relativePath,
            System::DateTime creationTime,
            System::DateTime lastAccessTime,
            System::DateTime lastWriteTime,
            System::DateTime changeTime,
            unsigned long fileAttributes,
            long long endOfFile,
            array<System::Byte>^ contentId,
            array<System::Byte>^ epochId,
            UpdateType updateFlags,
            UpdateFailureCause% failureReason);

        /// <summary>Completes a command\request that previously returned NtStatus::Pending</summary>
        /// <param name="commandId">The ID of the command to complete</param>
        /// <param name="completionStatus">Result of processing the command</param>
        void CompleteCommand(
            long commandId,
            NtStatus completionStatus);

        /// <summary>Create a WriteBuffer (to be used with WriteFile)</summary>
        /// <param name="desiredBufferSize">Desired size, in bytes, of the write buffer</param>
        /// <returns>A newly created WriteBuffer</returns>
        /// <remarks>
        /// Actual buffer size depends on the number of logical bytes per sector reported by physical storage:
        /// If desiredBufferSize is less than the logical bytes per sector the actual buffer size will be logical bytes per sector
        /// If desiredBufferSize is greater than logical bytes per sector the actual buffer size will be desiredBufferSize
        /// rounded up to the nearest multiple of logical bytes per sector
        /// </remarks>
        WriteBuffer^ CreateWriteBuffer(unsigned long desiredBufferSize);
    };
}