#pragma once

namespace GvLib
{
    ///<summary>Subset of NTSTATUS values</summary>
    public enum class NtStatus : NTSTATUS
    {
        Success = STATUS_SUCCESS,
        Timeout = STATUS_TIMEOUT,
        FileNotAvailable = STATUS_FILE_NOT_AVAILABLE,
        Unsuccessful = STATUS_UNSUCCESSFUL,
        NotImplemented = STATUS_NOT_IMPLEMENTED,
        InvalidHandle = STATUS_INVALID_HANDLE,
        InvalidParameter = STATUS_INVALID_PARAMETER,
        ObjectNameNotFound = STATUS_OBJECT_NAME_NOT_FOUND,
        ObjectPathNotFound = STATUS_OBJECT_PATH_NOT_FOUND,
        InvalidDeviceRequest = STATUS_INVALID_DEVICE_REQUEST,
        EndOfFile = STATUS_END_OF_FILE,
        BufferOverflow = STATUS_BUFFER_OVERFLOW,
        InternalError = STATUS_INTERNAL_ERROR,
        NoMemory = STATUS_NO_MEMORY,
        NoMoreFiles = STATUS_NO_MORE_FILES,
        NoSuchFile = STATUS_NO_SUCH_FILE,
        RequestAborted = STATUS_REQUEST_ABORTED,
        AccessDenied = STATUS_ACCESS_DENIED,
        NoInterface = STATUS_NOINTERFACE,
        DeviceNotReady = STATUS_DEVICE_NOT_READY,
        FileClosed = STATUS_FILE_CLOSED,
        ObjectNameInvalid = STATUS_OBJECT_NAME_INVALID,
        DirectoryNotEmpty = STATUS_DIRECTORY_NOT_EMPTY,
        CannotDelete = STATUS_CANNOT_DELETE,
        IoReparseTagNotHandled = STATUS_IO_REPARSE_TAG_NOT_HANDLED,
        DirectoryIsAReparsePoint = STATUS_DIRECTORY_IS_A_REPARSE_POINT,
        SharingViolation = STATUS_SHARING_VIOLATION,
        DeletePending = STATUS_DELETE_PENDING,
        FileSystemVirtualizationInvalidOperation = STATUS_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION,
        InsufficientResources = STATUS_INSUFFICIENT_RESOURCES,
        Pending = STATUS_PENDING,
        AccessViolation = STATUS_ACCESS_VIOLATION,
        NotLocked = STATUS_NOT_LOCKED,
        InfoLengthMismatch = STATUS_INFO_LENGTH_MISMATCH,
        StackOverflow = STATUS_STACK_OVERFLOW,
        EntrypointNotFound = STATUS_ENTRYPOINT_NOT_FOUND,
        MoreEntries = STATUS_MORE_ENTRIES,
        IntegerOverflow = STATUS_INTEGER_OVERFLOW,
        NoMoreEntries = STATUS_NO_MORE_ENTRIES,
        DiskFull = STATUS_DISK_FULL,
        NotADirectory = STATUS_NOT_A_DIRECTORY,
        FileInvalid = STATUS_FILE_INVALID,
        IoDeviceError = STATUS_IO_DEVICE_ERROR
    };
}