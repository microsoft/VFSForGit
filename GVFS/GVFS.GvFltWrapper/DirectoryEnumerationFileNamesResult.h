#pragma once

#include "DirectoryEnumerationResult.h"
#include "NativeEnumerationResultUtils.h"

namespace GvLib
{
    private ref class DirectoryEnumerationFileNamesResult : public DirectoryEnumerationResult
    {
    public:
        DirectoryEnumerationFileNamesResult(PFILE_NAMES_INFORMATION enumerationData, unsigned long maxEnumerationDataLength);

        property System::DateTime CreationTime
        {
            virtual void set(System::DateTime value) override { UNREFERENCED_PARAMETER(value); }
        };

        property System::DateTime LastAccessTime
        {
            virtual void set(System::DateTime value) override { UNREFERENCED_PARAMETER(value); }
        };

        property System::DateTime LastWriteTime
        {
            virtual void set(System::DateTime value) override { UNREFERENCED_PARAMETER(value); }
        };

        property System::DateTime ChangeTime
        {
            virtual void set(System::DateTime value) override { UNREFERENCED_PARAMETER(value); }
        };

        property long long EndOfFile
        {
            virtual void set(long long value) override { UNREFERENCED_PARAMETER(value); }
        };

        property unsigned int FileAttributes
        {
            virtual void set(unsigned int value) override { UNREFERENCED_PARAMETER(value); }
        };

        virtual bool TrySetFileName(System::String^ value) override;

    private:
        PFILE_NAMES_INFORMATION enumerationData;
        unsigned long maxEnumerationDataLength;
    };


    inline DirectoryEnumerationFileNamesResult::DirectoryEnumerationFileNamesResult(PFILE_NAMES_INFORMATION enumerationData, unsigned long maxEnumerationDataLength)
        : enumerationData(enumerationData)
        , maxEnumerationDataLength(maxEnumerationDataLength)
    {
        this->bytesWritten = FIELD_OFFSET(FILE_NAMES_INFORMATION, FileName);
    }

    inline bool DirectoryEnumerationFileNamesResult::TrySetFileName(System::String^ value)
    {
        bool nameTruncated = false;
        this->bytesWritten = PopulateNameInEnumerationData(this->enumerationData, this->maxEnumerationDataLength, value, nameTruncated);
        return !nameTruncated;
    }
}