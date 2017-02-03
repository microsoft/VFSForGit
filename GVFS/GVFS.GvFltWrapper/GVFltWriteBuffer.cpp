#include "stdafx.h"
#include "GvFltWriteBuffer.h"

using namespace System;
using namespace System::IO;
using namespace GVFSGvFltWrapper;

GVFltWriteBuffer::GVFltWriteBuffer(int bufferSize)
{
    this->buffer = (unsigned char*)malloc(sizeof(unsigned char) * bufferSize);
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
    free(this->buffer);
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
