#pragma once
#include "StatusCode.h"

namespace GVFSGvFltWrapper
{
    [System::Serializable()]
    public ref class GvFltException : System::Exception
    {
    public:
        GvFltException(System::String^ errorMessage);
        GvFltException(StatusCode errorCode);
        GvFltException(System::String^ errorMessage, StatusCode errorCode);

        virtual System::String^ ToString() override;

        virtual property StatusCode ErrorCode
        {
            StatusCode get(void);
        };

    private:
        StatusCode errorCode;
    };
}