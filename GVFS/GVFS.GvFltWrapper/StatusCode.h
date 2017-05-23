#pragma once

namespace GVFSGvFltWrapper
{
    public enum class StatusCode : long
    {
        // Subset of NTSTATUS values.  Add more values as needed.
        StatusSucccess = STATUS_SUCCESS,
        StatusTimeout = STATUS_TIMEOUT,
        StatusFileNotAvailable = STATUS_FILE_NOT_AVAILABLE,
        StatusUnsuccessful = STATUS_UNSUCCESSFUL,
        StatusNotImplemented = STATUS_NOT_IMPLEMENTED,
        StatusInvalidHandle = STATUS_INVALID_HANDLE,
        StatusInvalidParameter = STATUS_INVALID_PARAMETER,
        StatusObjectNameNotFound = STATUS_OBJECT_NAME_NOT_FOUND,
        StatusObjectPathNotFound = STATUS_OBJECT_PATH_NOT_FOUND,
        StatusInvalidDeviceRequest = STATUS_INVALID_DEVICE_REQUEST,
        StatusEndOfFile = STATUS_END_OF_FILE,
        StatusBufferOverflow = STATUS_BUFFER_OVERFLOW,
        StatusInternalError = STATUS_INTERNAL_ERROR,
        StatusNoMemory = STATUS_NO_MEMORY,
        StatusNoMoreFiles = STATUS_NO_MORE_FILES,
        StatusNoSuchFile = STATUS_NO_SUCH_FILE,
        StatusRequestAborted = STATUS_REQUEST_ABORTED,
        StatusAccessDenied = STATUS_ACCESS_DENIED,
        StatusNoInterface = STATUS_NOINTERFACE,
        StatusDeviceNotReady = STATUS_DEVICE_NOT_READY,
        StatusFileClosed = STATUS_FILE_CLOSED,
        StatusObjectNameInvalid = STATUS_OBJECT_NAME_INVALID,
        StatusDirectoryNotEmpty = STATUS_DIRECTORY_NOT_EMPTY,
        StatusCannotDelete = STATUS_CANNOT_DELETE,
        StatusIoReparseTagNotHandled = STATUS_IO_REPARSE_TAG_NOT_HANDLED,
        StatusDirectoryIsAReparsePoint = STATUS_DIRECTORY_IS_A_REPARSE_POINT,
        StatusSharingViolation = STATUS_SHARING_VIOLATION,
        StatusDeletePending = STATUS_DELETE_PENDING,
        StatusFileSystemVirtualizationInvalidOperation = STATUS_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION 
    };
}