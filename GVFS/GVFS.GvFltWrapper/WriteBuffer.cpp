#include "stdafx.h"
#include "WriteBuffer.h"

using namespace GvLib;
using namespace System;
using namespace System::IO;


WriteBuffer::WriteBuffer(ULONG bufferSize, ULONG alignment)
{
    this->buffer = (unsigned char*)_aligned_malloc(bufferSize, alignment);
    if (this->buffer == nullptr)
    {
        throw gcnew InvalidOperationException("Unable to allocate WriteBuffer");
    }

    this->stream = gcnew UnmanagedMemoryStream(buffer, bufferSize, bufferSize, FileAccess::Write);
}

WriteBuffer::~WriteBuffer()
{
    delete this->stream;
    this->!WriteBuffer();
}

WriteBuffer::!WriteBuffer()
{
    _aligned_free(this->buffer);
}

long long WriteBuffer::Length::get(void)
{
    return this->stream->Length;
}

UnmanagedMemoryStream^ WriteBuffer::Stream::get(void)
{
    return this->stream;
}

IntPtr WriteBuffer::Pointer::get(void)
{
    return IntPtr(this->buffer);
}
