#pragma once

namespace GVFSGvFltWrapper
{
    public enum class HResult : long
    {
        // Subset of HRESULT values.  Add more values as needed.

        Ok = S_OK,                        // Operation successful
        Abort = E_ABORT,                  // Operation aborted
        AccessDenied = E_ACCESSDENIED,    // General access denied error
        Fail = E_FAIL,                    // Unspecified failure
        Handle = E_HANDLE,                // Handle that is not valid
        InvalidArg = E_INVALIDARG,        // One or more arguments are not valid
        NoInterface = E_NOINTERFACE,      // No such interface supported
        NotImpl = E_NOTIMPL,              // Not implemented
        OutOfMemory = E_OUTOFMEMORY,      // Failed to allocate necessary memory
        Pointer = E_POINTER,              // Pointer that is not valid
        Unexpected = E_UNEXPECTED,        // Unexpected failure
        ReparsePointEncountered = __HRESULT_FROM_WIN32(ERROR_REPARSE_POINT_ENCOUNTERED) // The object manager encountered a reparse point while retrieving an object.
    };
}