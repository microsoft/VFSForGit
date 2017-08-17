#pragma once

#include "CallbackDelegates.h"
#include "UpdateFailureCause.h"
#include "HResult.h"
#include "ITracer.h"

namespace GvFlt 
{
    public ref class VirtualizationInstance
    {
    public:
        /// <summary>Length of ContentID and EpochID in bytes</summary>
        static const int PlaceholderIdLength = GV_PLACEHOLDER_ID_LENGTH;

        VirtualizationInstance();

        /// <summary>Start directory enumeration callback</summary>
        /// <seealso cref="StartDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property StartDirectoryEnumerationEvent^ OnStartDirectoryEnumeration
        {
            StartDirectoryEnumerationEvent^ get(void);
            void set(StartDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>End directory enumeration callback</summary>
        /// <seealso cref="EndDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property EndDirectoryEnumerationEvent^ OnEndDirectoryEnumeration
        {
            EndDirectoryEnumerationEvent^ get(void);
            void set(EndDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>Get next enumeration result callback</summary>
        /// <seealso cref="GetDirectoryEnumerationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetDirectoryEnumerationEvent^ OnGetDirectoryEnumeration
        {
            GetDirectoryEnumerationEvent^ get(void);
            void set(GetDirectoryEnumerationEvent^ eventCB);
        };

        /// <summary>Query file name callback</summary>
        /// <seealso cref="QueryFileNameEvent"/>
        /// <remarks>This callback is required</remarks>
        property QueryFileNameEvent^ OnQueryFileName
        {
            QueryFileNameEvent^ get(void);
            void set(QueryFileNameEvent^ eventCB);
        }

        /// <summary>Get placeholder callback</summary>
        /// <seealso cref="GetPlaceholderInformationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetPlaceholderInformationEvent^ OnGetPlaceholderInformation
        {
            GetPlaceholderInformationEvent^ get(void);
            void set(GetPlaceholderInformationEvent^ eventCB);
        };

        /// <summary>Get file stream callback</summary>
        /// <seealso cref="GetPlaceholderInformationEvent"/>
        /// <remarks>This callback is required</remarks>
        property GetFileStreamEvent^ OnGetFileStream
        {
            GetFileStreamEvent^ get(void);
            void set(GetFileStreamEvent^ eventCB);
        };

        /// <summary>First write notification callback</summary>
        /// <seealso cref="NotifyFirstWriteEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFirstWriteEvent^ OnNotifyFirstWrite
        {
            NotifyFirstWriteEvent^ get(void);
            void set(NotifyFirstWriteEvent^ eventCB);
        };

        /// <summary>File handle created notification callback</summary>
        /// <seealso cref="NotifyFileHandleCreatedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileHandleCreatedEvent^ OnNotifyFileHandleCreated
        {
            NotifyFileHandleCreatedEvent^ get(void);
            void set(NotifyFileHandleCreatedEvent^ eventCB);
        };

        /// <summary>Pre-delete notification callback</summary>
        /// <seealso cref="NotifyPreDeleteEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreDeleteEvent^ OnNotifyPreDelete
        {
            NotifyPreDeleteEvent^ get(void);
            void set(NotifyPreDeleteEvent^ eventCB);
        }

        /// <summary>Pre-rename notification callback</summary>
        /// <seealso cref="NotifyPreRenameEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreRenameEvent^ OnNotifyPreRename
        {
            NotifyPreRenameEvent^ get(void);
            void set(NotifyPreRenameEvent^ eventCB);
        }

        /// <summary>Pre-set-hardlink notification callback</summary>
        /// <seealso cref="NotifyPreSetHardlinkEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyPreSetHardlinkEvent^ OnNotifyPreSetHardlink
        {
            NotifyPreSetHardlinkEvent^ get(void);
            void set(NotifyPreSetHardlinkEvent^ eventCB);
        }

        /// <summary>File renamed notification callback</summary>
        /// <seealso cref="NotifyFileRenamedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileRenamedEvent^ OnNotifyFileRenamed
        {
            NotifyFileRenamedEvent^ get(void);
            void set(NotifyFileRenamedEvent^ eventCB);
        }

        /// <summary>Hardlink created notification callback</summary>
        /// <seealso cref="NotifyHardlinkCreatedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyHardlinkCreatedEvent^ OnNotifyHardlinkCreated
        {
            NotifyHardlinkCreatedEvent^ get(void);
            void set(NotifyHardlinkCreatedEvent^ eventCB);
        }

        /// <summary>File handle closed notification callback</summary>
        /// <seealso cref="NotifyFileHandleClosedEvent"/>
        /// <remarks>This callback is optional</remarks>
        property NotifyFileHandleClosedEvent^ OnNotifyFileHandleClosed
        {
            NotifyFileHandleClosedEvent^ get(void);
            void set(NotifyFileHandleClosedEvent^ eventCB);
        }

        /// <summary>Tracer to be used by VirtualizationInstance</summary>
        /// <seealso cref="ITracer"/>
        /// <remarks>Cannot be null, an ITracer must be set.</remarks>
        property ITracer^ Tracer
        {
            ITracer^ get(void);
        };
        
        /// <summary>Starts a GvFlt virtualization instance</summary>
        /// <param name="tracerImpl">ITracer implementation used to trace messages from the VirtualizationInstance.  Cannot be nullptr.</param>
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
        /// <exception cref="GvFltException">Thrown if provider already has a running virtualization instance</exception> 
        HResult StartVirtualizationInstance(
            ITracer^ tracerImpl,
            System::String^ virtualizationRootPath,
            unsigned long poolThreadCount,
            unsigned long concurrentThreadCount);

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

        enum class OnDiskStatus : long
        {
            NotOnDisk = 0,
            Partial = 1,
            Full = 2,
            OnDiskCannotOpen = 3
        };

        /// <summary>Checks if file is on disk, and whether it's partial or full</summary>
        /// <param name="relativePath">The path (relative to the virtualization root) of the file</param>
        /// <returns>OnDiskStatus indicating if the file is not on disk, a partial file, or a full file</returns>
        /// <remarks>
        /// This function cannot be used to determine if a folder is partial or full, and cannot be
        /// used to determine if a path is a file or a folder.
        /// </remarks>
        /// <exception cref="GvFltException">Thrown when an error is encountered while trying to open the file</exception> 
        OnDiskStatus GetFileOnDiskStatus(System::String^ relativePath);

        /// <summary>Returns the full contents of a file</summary>
        /// <param name="relativePath">The path (relative to the virtualization root) of the file</param>
        /// <returns>Contents of the specified full file.  BOM, if present, is not removed.</returns>
        /// <exception cref="GvFltException">Thrown when unable to open the file</exception> 
        System::String^ ReadFullFileContents(System::String^ relativePath);

        /// <summary>Create a WriteBuffer (to be used with WriteFile)</summary>
        /// <returns>A newly created WriteBuffer</returns>
        WriteBuffer^ CreateWriteBuffer();

        /// <summary>Converts an existing folder to a GvFlt virtualization root</summary>
        /// <param name="virtualizationInstanceGuid">The Guid that uniquely identifies one virtualization instance</param>
        /// <param name="rootPath">Path for the virtualization instance root directory</param>
        /// <returns>
        /// If ConvertDirectoryToVirtualizationRoot succeeds, Ok is returned.
        /// 
        /// If rootPath is a file and not a directory, InvalidArg is returned.
        /// 
        /// If rootPath already contains reparsepoint data, ReparsePointEncountered is returned.
        /// 
        /// If rootPath fails to open, the appropriate error is returned.
        /// </returns>
        static HResult ConvertDirectoryToVirtualizationRoot(
            System::Guid virtualizationInstanceGuid, 
            System::String^ rootPath);        

    private:
        ULONG GetWriteBufferSize();
        ULONG GetAlignmentRequirement();

        void ConfirmNotStarted();
        void CalculateWriteBufferSizeAndAlignment();

        StartDirectoryEnumerationEvent^ startDirectoryEnumerationEvent;
        EndDirectoryEnumerationEvent^ endDirectoryEnumerationEvent;
        GetDirectoryEnumerationEvent^ getDirectoryEnumerationEvent;
        QueryFileNameEvent^ queryFileNameEvent;
        GetPlaceholderInformationEvent^ getPlaceholderInformationEvent;
        GetFileStreamEvent^ getFileStreamEvent;
        NotifyFirstWriteEvent^ notifyFirstWriteEvent;
        NotifyFileHandleCreatedEvent^ notifyFileHandleCreatedEvent;
        NotifyPreDeleteEvent^ notifyPreDeleteEvent;
        NotifyPreRenameEvent^ notifyPreRenameEvent;
        NotifyPreSetHardlinkEvent^ notifyPreSetHardlinkEvent;
        NotifyFileRenamedEvent^ notifyFileRenamedEvent;
        NotifyHardlinkCreatedEvent^ notifyHardlinkCreatedEvent;
        NotifyFileHandleClosedEvent^ notifyFileHandleClosedEvent;

        ULONG writeBufferSize;
        ULONG alignmentRequirement;

        GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle;
        System::String^ virtualRootPath;
        ITracer^ tracer;
    };
}
