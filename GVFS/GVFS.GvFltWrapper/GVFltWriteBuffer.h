#pragma once

namespace GVFSGvFltWrapper
{
    public ref class GVFltWriteBuffer
    {
    public:
        GVFltWriteBuffer(int bufferSize);
        ~GVFltWriteBuffer();

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
        !GVFltWriteBuffer();

    private:
        System::IO::UnmanagedMemoryStream^ stream;
        unsigned char* buffer;
    };
}