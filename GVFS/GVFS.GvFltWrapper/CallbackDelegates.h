#pragma once

#include "DirectoryEnumerationResult.h"
#include "NtStatus.h"
#include "WriteBuffer.h"

namespace GvFlt
{
    /// <summary>Directory is about to be enumerated</summary>
    /// <param name="enumerationId">The Guid value associated with this directory path for a set of enumeration commands</param>
    /// <param name="relativePath">The path (relative to the virtualization root) to be enumerated</param>
    /// <returns>Success if callback succeeded, appropriate error otherwise</returns>
    public delegate NtStatus StartDirectoryEnumerationEvent(
        System::Guid enumerationId, 
        System::String^ relativePath);

    /// <summary>Directory enumeration is complete</summary>
    /// <param name="enumerationId">The Guid value associated with this directory path for a set of enumeration commands</param>
    /// <returns>Success if callback succeeded, appropriate error otherwise</returns>
    public delegate NtStatus EndDirectoryEnumerationEvent(System::Guid enumerationId);
    
    /// <summary>Gets the next DirectoryEnumerationResult for the specified enumeration</summary>
    /// <param name="enumerationId">The Guid value to associate with this directory path for a set of enumeration commands</param>
    /// <param name="filterFileName">
    /// An optional string containing the name of a file (or multiple files, if wildcards are used) within the directory.  
    /// This parameter is optional and can be nullptr.
    ///     If filterFileName is not nullptr, only files whose names match the filterFileName string should be included in the directory scan.
    ///     If filterFileName is nullptr, all files should be included
    ///</param>
    /// <param name="restartScan">true if the scan is to start at the first entry in the directory. false if resuming the scan from a previous call.</param>
    /// <param name="result">Next DirectoryEnumerationResult to return in the enumeration result</param>
    /// <returns>
    /// NtStatus indicating the result of the callback:
    ///
    ///     Success        - result set successfully
    ///     NoSuchFile     - No file matches the specified filter.  (Note: NoSuchFile should only be returned when filterFileName is set 
    ///                      (i.e.non - empty and not '*') and only for the first request for the specified filterFileName)
    ///     NoMoreFiles    - No more files match the specified filter (or if no filter is set, there are no files)
    ///     BufferOverflow - File name of the next result does not fit in result (i.e. DirectoryEnumerationResult.TrySetFileName failed).
    ///     (Or appropriate error in case of failure)
    /// </returns>
    /// <remarks>
    ///     - filterFileName must be persisted across calls to GetDirectoryEnumerationEvent, and only be reset when restartScan is true
    ///     - If BufferOverflow is returned, the enumeration should not be advanced (and the next call to GetDirectoryEnumerationEvent
    ///       should return the entry that was previously too large to fit in the result)
    /// </remarks>
    public delegate NtStatus GetDirectoryEnumerationEvent(
        System::Guid enumerationId,
        System::String^ filterFileName,
        bool restartScan,
        DirectoryEnumerationResult^ result);
    
    /// <summary>Checks if a relative file path exists in the provider's backing layer</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file being queried</param>
    /// <returns>
    /// If relativePath exists in the provider's backing layer, return Success, otherwise return ObjectNameNotFound.
    /// If a failure occurs, return the appropriate error.
    /// </returns>
    public delegate NtStatus QueryFileNameEvent(System::String^ relativePath);

    /// <summary>Request for placeholder information</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file to return information for</param>
    /// <param name="desiredAccess">The requested access to the file or folder.  See CreateFile API on MSDN for the possible values</param>
    /// <param name="shareMode">The requested sharing mode of the file or folder</param>
    /// <param name="createDisposition">The requested create disposition</param>
    /// <param name="createOptions">
    /// The requested options to be applied when creating or opening the file.
    /// Please refer to the MSDN page for NtCreateFile for more details and possible values for
    /// DesiredAccess, ShareMode, CreateDisposition and CreateOptions parameters -
    /// https://msdn.microsoft.com/en-us/library/bb432380(v=vs.85).aspx
    /// </param>
    /// <param name="triggeringProcessId">The PID for the process that triggered this callback</param>
    /// <param name="triggeringProcessImageFileName">The image file name for triggeringProcessId</param>
    /// <returns>Success if callback succeeded, appropriate error otherwise</returns>
    /// <remarks>
    /// In this callback, a single call to WritePlaceholderInformation should be made to send all the information 
    /// for creating the placeholder for the filename requested.  Returning from callback signals the GvFlt driver that all 
    /// information needed to create a placeholder was provided to GvFlt by a successful call to WritePlaceholderInformation.
    /// </remarks>
    public delegate NtStatus GetPlaceholderInformationEvent(
        System::String^ relativePath,
        unsigned long desiredAccess,
        unsigned long shareMode,
        unsigned long createDisposition,
        unsigned long createOptions,
        unsigned long triggeringProcessId,
        System::String^ triggeringProcessImageFileName);
    
    /// <summary>Request for the file stream contents for creating the file stream on disk</summary>
    /// <param name="relativePath">
    /// The path (relative to the virtualization root) of the file to return file contents for.  If a file
    /// has been renamed or moved, relativePath will be its original path (prior to move\rename).
    /// </param>
    /// <param name="byteOffset">Requested byte offset of the stream content. Always 0 in the current version.</param>
    /// <param name="length">Requested number of bytes of the stream content. Always equal to the full stream length in the current version.</param>
    /// <param name="streamGuid">
    /// The Guid value to associate with this file stream for a set of WriteFile commands.
    /// The provider mush pass in this Guid value when calling WriteFile in the callback for the same file stream.
    /// </param>
    /// <param name="contentId">ContentId of the placeholder</param>
    /// <param name="epochId">EpochId of the placeholder</param>
    /// <param name="triggeringProcessId">The PID for the process that triggered this callback</param>
    /// <param name="triggeringProcessImageFileName">The image file name for triggeringProcessId</param>
    /// <returns>Success if callback succeeded, appropriate error otherwise</returns>
    /// <remarks>
    /// In this callback, the provider will make a single or multiple calls to WriteFile, to send the main file stream content for the file name requested.
    /// Returning from the callback signals the GvFlt driver that all file stream content has been provided to GvFlt by a successful call to WriteFile.
    /// </remarks>
    public delegate NtStatus GetFileStreamEvent(
        System::String^ relativePath,
        long long byteOffset,
        unsigned long length,
        System::Guid streamGuid,
        array<System::Byte>^ contentId,
        array<System::Byte>^ epochId,
        unsigned long triggeringProcessId,
        System::String^ triggeringProcessImageFileName);
    
    /// <summary>A file or folder has been written to for the first time</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <returns>Success if callback succeeded, appropriate error otherwise</returns>
    /// <remarks>
    /// Returning from the callback signals the GvFlt driver that the provider has completed all necessary 
    /// bookkeeping and the write operation can proceed.
    /// 
    /// Note -
    /// Returning an error from the callback will cause GvFlt driver to fail to proceed and therefore fail the user request
    /// that triggers this callback.
    /// 
    /// This callback is triggered on below operations -
    /// Note - the callback is sent on an attempt to issue the operation to the file system,
    ///        not after the operation has actually succeeded.
    /// 
    /// 1) Open(CreateFile)
    ///     a) A callback with the path to the target file / directory will be sent if FILE_WRITE_DATA, FILE_APPEND_DATA or
    ///     FILE_WRITE_ATTRIBUTES is set in the access mask
    /// 
    /// 2) Delete(DeleteFile, RemoveDirectory or using DELETE_ON_CLOSE in CreateFile)
    ///     a) A callback with the path to the parent directory will be sent
    /// 
    /// 3) Rename(rename, movefile)
    ///     a) A callback with the path to the source's parent directory will be sent
    ///     b) A callback with the path to the destination's parent directory will be sent
    /// 
    /// 4) New file/folder(CreateFile)
    ///     a) A callback with the path to the parent directory will be sent
    /// 
    /// GvFlt guarantees that below properties for this callback is true at all the times -
    /// 1) The placeholder file for the path file name passed in in the callback must have been created on disk
    /// 2) Only one callback will be sent for one file / directory during the lifetime of the virtualization root
    ///    (from the virtualization root folder is created to it's deleted)
    /// </remarks>
    public delegate NtStatus NotifyFirstWriteEvent(System::String^ relativePath);

    /// <summary>A handle to a file or folder has been created</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="isDirectory">true if relativePath is for a folder, false if relativePath is for a file</param>
    /// <param name="desiredAccess">Desired access specified for handle</param>
    /// <param name="shareMode">Share mode specified for handle</param>
    /// <param name="createDisposition">Create disposition specified for handle</param>
    /// <param name="createOptions">Create options specified for handle</param>
    /// <param name="ioStatusBlock">Final completion status of the handle create operation</param>
    /// <param name="notificationMask">
    /// [Out] A bit mask that indicates which notifications the provider wants to watch for the target file.
    /// Refer to the NotificationType enum for a list of notifications the provider can watch.
    /// If this field is not set, no notification will be watched.
    /// </param>
    /// <remarks>
    /// Refer to the MSDN page for NtCreateFile for more details and possible values for
    /// desiredAccess, shareMode, createDisposition, createOptions and ioStatusBlock parameters -
    /// https://msdn.microsoft.com/en-us/library/bb432380(v=vs.85).aspx
    ///</remarks>
    public delegate void NotifyFileHandleCreatedEvent(
        System::String^ relativePath,
		bool isDirectory,
        unsigned long desiredAccess,
        unsigned long shareMode,
        unsigned long createDisposition,
        unsigned long createOptions,
        IoStatusBlockValue ioStatusBlock,
        unsigned long% notificationMask);

    /// <summary>An attempt to delete a watched file or directory is made</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="isDirectory">true if relativePath is for a folder, false if relativePath is for a file</param>
    /// <returns>Success if the delete should be allowed to proceed, an error code if the delete should be prevented</returns>
    /// <remarks>
    /// This is 
    /// 1) The pre-operation callback for FileDispositionInformation or 
    ///    FileDispositionInformationEx with DeleteFile set to TRUE.
    /// OR
    /// 2) Pre-operation for IRP_MJ_CLEANUP if the handle was opened with DELETE_ON_CLOSE.
    /// 
    /// GvFlt won't send this notification if someone tries to undo the delete by setting 
    /// DeleteFile to FALSE for FileDispositionInformation.
    /// </remarks>
    public delegate NtStatus NotifyPreDeleteEvent(
        System::String^ relativePath, 
        bool isDirectory);

    /// <summary>
    /// An attempt to rename a watched file or directory, or move a file or directory 
    /// into the virtualization root is made
    /// </summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="destinationPath">Destination path (relative to the virtualization root)</param>
    /// <returns>Success if the rename should be allowed to proceed, an error code if the rename should be prevented</returns>
    /// <remarks>
    /// This is the pre-operation callback for IRP_MJ_SET_INFORMATION with FileRenameInformation
    /// or FileRenameInformationEx file information class.
    ///
    /// The provider won't receive the rename notification if GvFlt decided to fail the request.
    /// eg. an attempt to rename a placeholder folder, or the path name to be renamed exists 
    /// in the backing layer.
    ///
    /// If moving a file from outside to inside the instance, relativePath will be ""
    /// If moving a file from inside to outside the instance, destinationPath will be ""
    /// If moving a file between 2 instances, each instance will receive one rename
    /// notification if it's being watched, one with relativePath being "" and the other with 
    /// destinationPath being "".
    ///
    /// The provider won't receive any rename notification for renaming a stream.
    /// </remarks>
    public delegate NtStatus NotifyPreRenameEvent(
        System::String^ relativePath, 
        System::String^ destinationPath);
    
    /// <summary>An attempt to create a hardlink for a watched file is made.</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file</param>
    /// <param name="destinationPath">Destination file name</param>
    /// <returns>Success if the hardlink operation should be allowed to proceed, an error code if the operation should be prevented</returns>
    /// <remarks>This is the pre-operation callback for IRP_MJ_SET_INFORMATION with FileLinkInformation file information class</remarks>
    public delegate NtStatus NotifyPreSetHardlinkEvent(
        System::String^ relativePath, 
        System::String^ destinationPath);

    /// <summary>A watched file or directory was renamed, or a file or directory was moved into the virtualization root</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="destinationPath">Destination path (relative to the virtualization root)</param>
    /// <param name="isDirectory">true if relativePath is for a folder, false if relativePath is for a file</param>
    /// <param name="notificationMask">
    /// [InOut] A bit mask that indicates which notifications the provide wants to watch for the renamed file.
    /// If this field is not set, the notification mask of the source file will be used.
    /// </param>
    /// <remarks>
    /// Paths will be empty strings when location is outside of the virtualization root.
    /// This is the post-operation callback for IRP_MJ_SET_INFORMATION with FileRenameInformation
    /// or FileRenameInformationEx file information class.  If the rename opeartion failed, eg. the destination file already exists, 
    /// the provider won't receive this notification.
    /// </remarks>
    public delegate void NotifyFileRenamedEvent(
        System::String^ relativePath, 
        System::String^ destinationPath, 
        bool isDirectory, 
        unsigned long% notificationMask);

    /// <summary>A hardlink within the virtualization root was created</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="destinationPath">Destination file name</param>
    /// <remarks>This is the post-operation callback for IRP_MJ_SET_INFORMATION with FileLinkInformation file information class</remarks>
    public delegate void NotifyHardlinkCreatedEvent(
        System::String^ relativePath, 
        System::String^ destinationPath);

    /// <summary>A handle to a watched file or directory was closed</summary>
    /// <param name="relativePath">The path (relative to the virtualization root) of the file or folder</param>
    /// <param name="isDirectory">true if relativePath is for a folder, false if relativePath is for a file</param>
    /// <param name="fileModified">If true, a handle which was used to modify the file's main stream data was closed.</param>
    /// <param name="fileDeleted">If true, the file has been deleted from the file system</param>
    /// <remarks>
    /// fileModified is set to true if:
    ///    1) A cached write was made using the handle.
    /// Or 2) A non-cached write was made using the handle.
    /// Or 3) A writable section was created using the handle.
    /// 
    /// fileDeleted requires that the OS support the 'reliable delete information' feature, otherwise it will always be false.
    /// To check if the target volume supports this feature, the provider can retrieve the volume info and query if the flag below is set. 
    /// See also https://msdn.microsoft.com/en-us/library/windows/desktop/aa364993(v=vs.85).aspx
    ///
    /// #define FILE_RETURNS_CLEANUP_RESULT_INFO    0x00000200  // winnt
    /// </remarks>
    public delegate void NotifyFileHandleClosedEvent(
        System::String^ relativePath,
        bool isDirectory,
        bool fileModified, 
        bool fileDeleted);
}