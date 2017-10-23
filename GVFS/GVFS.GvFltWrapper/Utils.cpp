#include "stdafx.h"
#include "Utils.h"

using namespace GvLib;

NtStatus Utils::Win32ErrorToNtStatus(int win32Error)
{
    // Mapping is a combination of ToNTStatus in the GvFlt test app, and
    // mapping provided at https://support.microsoft.com/en-us/kb/113996.
    // Note that mapping on microsoft.com is not 1:1.  When a single win32error
    // can map to multiple NTSTATUS values, the more general NTSTATUS value is
    // returned.
    switch (win32Error)
    {
    case ERROR_INVALID_PARAMETER:
        return NtStatus::InvalidParameter;
    case ERROR_FILE_NOT_FOUND:
        return NtStatus::ObjectNameNotFound;
    case ERROR_ACCESS_DENIED:
        return NtStatus::AccessDenied;
    case ERROR_NOACCESS:
        return NtStatus::AccessViolation;
    case ERROR_NOT_LOCKED:
        return NtStatus::NotLocked; ;
    case ERROR_BAD_LENGTH:
        return NtStatus::InfoLengthMismatch;
    case ERROR_STACK_OVERFLOW:
        return NtStatus::StackOverflow;
    case ERROR_PROC_NOT_FOUND:
        return NtStatus::EntrypointNotFound;
    case ERROR_IO_PENDING:
        return NtStatus::Pending;
    case ERROR_MORE_DATA:
        return NtStatus::MoreEntries;  
    case ERROR_ARITHMETIC_OVERFLOW:
        return NtStatus::IntegerOverflow;
    case ERROR_NO_MORE_ITEMS:
        return NtStatus::NoMoreEntries;
    case ERROR_INVALID_HANDLE:
        return NtStatus::InvalidHandle;
    case ERROR_PATH_NOT_FOUND:
        return NtStatus::ObjectPathNotFound;
    case ERROR_DISK_FULL:
        return NtStatus::DiskFull; 
    case ERROR_DIRECTORY:
        return NtStatus::NotADirectory;
    case ERROR_FILE_INVALID:
        return NtStatus::FileInvalid;
    case ERROR_IO_DEVICE:
        return NtStatus::IoDeviceError; 
    default:
        return NtStatus::InternalError;
    }
}