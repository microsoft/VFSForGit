#pragma once

#include "DirectoryEnumerationResult.h"

namespace GvLib
{
    template<class NativeEnumerationDataStruct>
    private ref class DirectoryEnumerationResultImpl : public DirectoryEnumerationResult
    {
    public:
        DirectoryEnumerationResultImpl(NativeEnumerationDataStruct* enumerationData, unsigned long maxEnumerationDataLength);

        property System::DateTime CreationTime
        {
            virtual void set(System::DateTime value) override;
        };

        property System::DateTime LastAccessTime
        {
            virtual void set(System::DateTime value) override;
        };

        property System::DateTime LastWriteTime
        {
            virtual void set(System::DateTime value) override;
        };

        property System::DateTime ChangeTime
        {
            virtual void set(System::DateTime value) override;
        };

        property long long EndOfFile
        {
            virtual void set(long long value) override;
        };

        property unsigned int FileAttributes
        {
            virtual void set(unsigned int value) override;
        };

        virtual bool TrySetFileName(System::String^ value) override;

    private:
        NativeEnumerationDataStruct* enumerationData;
        unsigned long maxEnumerationDataLength;
    };


    template<class NativeEnumerationDataStruct>
    inline DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::DirectoryEnumerationResultImpl(NativeEnumerationDataStruct* enumerationData, unsigned long maxEnumerationDataLength)
        : enumerationData(enumerationData)
        , maxEnumerationDataLength(maxEnumerationDataLength)
    {
        this->bytesWritten = FIELD_OFFSET(NativeEnumerationDataStruct, FileName);

        // Projected files always have an allocation size of 0
        this->enumerationData->AllocationSize.QuadPart = 0;
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::CreationTime::set(System::DateTime value)
    {
        this->enumerationData->CreationTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::LastAccessTime::set(System::DateTime value)
    {
        this->enumerationData->LastAccessTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::LastWriteTime::set(System::DateTime value)
    {
        this->enumerationData->LastWriteTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::ChangeTime::set(System::DateTime value)
    {
        this->enumerationData->ChangeTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::EndOfFile::set(long long value)
    {
        this->enumerationData->EndOfFile.QuadPart = value;
    }

    template<class NativeEnumerationDataStruct>
    inline void DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::FileAttributes::set(unsigned int value)
    {
        this->enumerationData->FileAttributes = value;
    }

    template<class NativeEnumerationDataStruct>
    inline bool DirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::TrySetFileName(System::String^ value)
    {
        bool nameTruncated = false;
        this->bytesWritten = PopulateNameInEnumerationData(this->enumerationData, this->maxEnumerationDataLength, value, nameTruncated);
        return !nameTruncated;
    }
}