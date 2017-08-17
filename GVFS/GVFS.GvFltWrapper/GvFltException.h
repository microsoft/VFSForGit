#pragma once

#include "NtStatus.h"

namespace GvFlt
{
    [System::Serializable()]
    public ref class GvFltException : System::Exception
    {
    public:
        GvFltException(System::String^ errorMessage);
        GvFltException(NtStatus errorCode);
        GvFltException(System::String^ errorMessage, NtStatus errorCode);

        virtual System::String^ ToString() override;

        virtual property NtStatus ErrorCode
        {
            NtStatus get(void);
        };

    private:
        NtStatus errorCode;
    };
}