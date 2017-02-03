#pragma once

namespace GVFSGvFltWrapper
{
    inline System::Guid GUIDtoGuid(const GUID& guid)
    {
        return System::Guid(
            guid.Data1,
            guid.Data2,
            guid.Data3,
            guid.Data4[0],
            guid.Data4[1],
            guid.Data4[2],
            guid.Data4[3],
            guid.Data4[4],
            guid.Data4[5],
            guid.Data4[6],
            guid.Data4[7]);
    }

    inline NTSTATUS Win32ErrorToNtStatus(int win32Error)
    {
        // Mapping is a combination of ToNTStatus in the GVFlt test app, and
        // mapping provided at https://support.microsoft.com/en-us/kb/113996.
        // Note that mapping on microsoft.com is not 1:1.  When a single win32error
        // can map to multiple NTSTATUS values, the more general NTSTATUS value is
        // returned.
        switch (win32Error)
        {
        case ERROR_INVALID_PARAMETER:
            return STATUS_INVALID_PARAMETER;
        case ERROR_FILE_NOT_FOUND:
            return STATUS_OBJECT_NAME_NOT_FOUND;
        case ERROR_ACCESS_DENIED:
            return STATUS_ACCESS_DENIED;
        case ERROR_NOACCESS:
            return STATUS_ACCESS_VIOLATION;
        case ERROR_NOT_LOCKED:
            return STATUS_NOT_LOCKED;
        case ERROR_BAD_LENGTH:
            return STATUS_INFO_LENGTH_MISMATCH;
        case ERROR_STACK_OVERFLOW:
            return STATUS_STACK_OVERFLOW;
        case ERROR_PROC_NOT_FOUND:
            return STATUS_ENTRYPOINT_NOT_FOUND;
        case ERROR_IO_PENDING:
            return STATUS_PENDING;
        case ERROR_MORE_DATA:
            return STATUS_MORE_ENTRIES;
        case ERROR_ARITHMETIC_OVERFLOW:
            return STATUS_INTEGER_OVERFLOW;
        case ERROR_NO_MORE_ITEMS:
            return STATUS_NO_MORE_ENTRIES;
        case ERROR_INVALID_HANDLE:
            return STATUS_INVALID_HANDLE;
        case ERROR_PATH_NOT_FOUND:
            return STATUS_OBJECT_PATH_NOT_FOUND;
        case ERROR_DISK_FULL:
            return STATUS_DISK_FULL;
        case ERROR_DIRECTORY:
            return STATUS_NOT_A_DIRECTORY;
        case ERROR_FILE_INVALID:
            return STATUS_FILE_INVALID;
        case ERROR_IO_DEVICE:
            return STATUS_IO_DEVICE_ERROR;
        default:
            return STATUS_INTERNAL_ERROR;
        }
    }
}