#pragma once

#include "NtStatus.h"

namespace GvLib
{
    public ref class Utils abstract sealed
    {
    public:
        static NtStatus Win32ErrorToNtStatus(int win32Error);
    };
}