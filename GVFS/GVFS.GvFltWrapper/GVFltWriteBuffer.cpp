#include "stdafx.h"
#include "GvFltWriteBuffer.h"

using namespace System;
using namespace System::IO;
using namespace GVFSGvFltWrapper;

GVFltWriteBuffer::GVFltWriteBuffer(ULONG bufferSize, ULONG alignment)
{
    this->buffer = (unsigned char*)_aligned_malloc(bufferSize, alignment);
    if (this->buffer == nullptr)
    {
        throw gcnew InvalidOperationException("Unable to allocate GVFltWriteBuffer");
    }

    this->stream = gcnew UnmanagedMemoryStream(buffer, bufferSize, bufferSize, FileAccess::Write);
}

GVFltWriteBuffer::~GVFltWriteBuffer()
{
    delete this->stream;
    this->!GVFltWriteBuffer();
}

GVFltWriteBuffer::!GVFltWriteBuffer()
{
    _aligned_free(this->buffer);
}

long long GVFltWriteBuffer::Length::get(void)
{
    return this->stream->Length;
}

UnmanagedMemoryStream^ GVFltWriteBuffer::Stream::get(void)
{
    return this->stream;
}

IntPtr GVFltWriteBuffer::Pointer::get(void)
{
    return IntPtr(this->buffer);
}
