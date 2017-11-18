#pragma once

namespace GvLib
{
    public ref class DirectoryEnumerationResult abstract
    {
    public:
        property System::DateTime CreationTime
        {
            virtual void set(System::DateTime value) abstract;
        };

        property System::DateTime LastAccessTime
        {
            virtual void set(System::DateTime value) abstract;
        };

        property System::DateTime LastWriteTime
        {
            virtual void set(System::DateTime value) abstract;
        };

        property System::DateTime ChangeTime
        {
            virtual void set(System::DateTime value) abstract;
        };

        property long long EndOfFile
        {
            virtual void set(long long value) abstract;
        };

        property unsigned int FileAttributes
        {
            virtual void set(unsigned int value) abstract;
        };

        property unsigned long BytesWritten
        {
            unsigned long get(void);
        }

        // Returns true if the entire name could be set, and false if the name had to be truncated
        // due to insufficient space
        virtual bool TrySetFileName(System::String^ value) abstract;

    protected:
        DirectoryEnumerationResult();

        unsigned long bytesWritten;

    };

    inline DirectoryEnumerationResult::DirectoryEnumerationResult()
        : bytesWritten(0)
    {
    }

    inline unsigned long DirectoryEnumerationResult::BytesWritten::get(void)
    {
        return this->bytesWritten;
    }
}