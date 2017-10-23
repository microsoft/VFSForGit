#pragma once

namespace GvLib
{
    public ref class WriteBuffer
    {
    public:
        WriteBuffer(ULONG bufferSize, ULONG alignment);
        ~WriteBuffer();

        property long long Length
        {
            long long get(void);
        };

        property System::IO::UnmanagedMemoryStream^ Stream
        {
            System::IO::UnmanagedMemoryStream^ get(void);
        };

        property System::IntPtr Pointer
        {
            System::IntPtr get(void);
        }

    protected:
        !WriteBuffer();

    private:
        System::IO::UnmanagedMemoryStream^ stream;
        unsigned char* buffer;
    };
}