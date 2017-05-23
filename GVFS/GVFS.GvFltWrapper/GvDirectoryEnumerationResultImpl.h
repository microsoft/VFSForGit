#pragma once

#include "GvDirectoryEnumerationResult.h"

namespace GVFSGvFltWrapper
{
    template<class NativeEnumerationDataStruct>
    public ref class GvDirectoryEnumerationResultImpl : public GvDirectoryEnumerationResult
    {
    public:
        GvDirectoryEnumerationResultImpl(NativeEnumerationDataStruct* enumerationData, unsigned long maxEnumerationDataLength);

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
    inline GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::GvDirectoryEnumerationResultImpl(NativeEnumerationDataStruct* enumerationData, unsigned long maxEnumerationDataLength)
        : enumerationData(enumerationData)
        , maxEnumerationDataLength(maxEnumerationDataLength)
    {
        this->bytesWritten = FIELD_OFFSET(NativeEnumerationDataStruct, FileName);

        // Projected files always have an allocation size of 0
        this->enumerationData->AllocationSize.QuadPart = 0;
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::CreationTime::set(System::DateTime value)
    {
        this->enumerationData->CreationTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::LastAccessTime::set(System::DateTime value)
    {
        this->enumerationData->LastAccessTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::LastWriteTime::set(System::DateTime value)
    {
        this->enumerationData->LastWriteTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::ChangeTime::set(System::DateTime value)
    {
        this->enumerationData->ChangeTime.QuadPart = value.ToFileTime();
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::EndOfFile::set(long long value)
    {
        this->enumerationData->EndOfFile.QuadPart = value;
    }

    template<class NativeEnumerationDataStruct>
    inline void GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::FileAttributes::set(unsigned int value)
    {
        this->enumerationData->FileAttributes = value;
    }

    template<class NativeEnumerationDataStruct>
    inline bool GvDirectoryEnumerationResultImpl<NativeEnumerationDataStruct>::TrySetFileName(System::String^ value)
    {
        bool nameTruncated = false;
        this->bytesWritten = PopulateNameInEnumerationData(this->enumerationData, this->maxEnumerationDataLength, value, nameTruncated);
        return !nameTruncated;
    }
}