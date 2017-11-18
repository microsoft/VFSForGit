#pragma once

#include "NtStatus.h"

namespace GvLib
{
    [System::Serializable]
    public ref class GvLibException : System::Exception
    {
    public:
        GvLibException(System::String^ errorMessage);
        GvLibException(NtStatus errorCode);
        GvLibException(System::String^ errorMessage, NtStatus errorCode);

        virtual System::String^ ToString() override;

        [System::Security::Permissions::SecurityPermission(
            System::Security::Permissions::SecurityAction::LinkDemand, 
            Flags = System::Security::Permissions::SecurityPermissionFlag::SerializationFormatter)]
        virtual void GetObjectData(
            System::Runtime::Serialization::SerializationInfo^ info,
            System::Runtime::Serialization::StreamingContext context) override;

        virtual property NtStatus ErrorCode
        {
            NtStatus get(void);
        };

    protected:
        GvLibException(
            System::Runtime::Serialization::SerializationInfo^ info, 
            System::Runtime::Serialization::StreamingContext context);

    private:
        NtStatus errorCode;
    };
}