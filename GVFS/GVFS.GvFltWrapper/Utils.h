#pragma once

#include "NtStatus.h"

namespace GvLib
{
    public ref class Utils abstract sealed
    {
    public:

        /// <summary>Map a Win32 error code to NtStatus value</summary>
        /// <param name="win32Error">Win32 error code</param>
        /// <returns>NtStatus value</returns>
        static NtStatus Win32ErrorToNtStatus(int win32Error);
    };
}