#pragma once

#include "GvDirectoryEnumerationResult.h"
#include "GVFltWriteBuffer.h"

namespace GVFSGvFltWrapper
{
    public delegate StatusCode GvStartDirectoryEnumerationEvent(System::Guid enumerationId, System::String^ relativePath);

    public delegate StatusCode GvEndDirectoryEnumerationEvent(System::Guid enumerationId);
    
    public delegate StatusCode GvGetDirectoryEnumerationEvent(
        System::Guid enumerationId,
        System::String^ filterFileName,
        bool restartScan,
        GvDirectoryEnumerationResult^ result);
    
    public delegate StatusCode GvQueryFileNameEvent(System::String^ relativePath);

    public delegate StatusCode GvGetPlaceHolderInformationEvent(
        System::String^ relativePath,
        unsigned long desiredAccess,
        unsigned long shareMode,
        unsigned long createDisposition,
        unsigned long createOptions,
        unsigned long triggeringProcessId,
        System::String^ triggeringProcessImageFileName);
    
    public delegate StatusCode GvGetFileStreamEvent(
        System::String^ relativePath,
        long long byteOffset,
        unsigned long length,
        System::Guid streamGuid,
        System::String^ contentId,
        unsigned long triggeringProcessId,
        System::String^ triggeringProcessImageFileName,
        GVFltWriteBuffer^ targetBuffer);
    
    public delegate StatusCode GvNotifyFirstWriteEvent(System::String^ relativePath);

    public delegate void GvNotifyCreateEvent(
        System::String^ relativePath,
		bool isDirectory,
        unsigned long desiredAccess,
        unsigned long shareMode,
        unsigned long createDisposition,
        unsigned long createOptions,
        IoStatusBlockValue ioStatusBlock,
        unsigned long% notificationMask);

    public delegate StatusCode GvNotifyPreDeleteEvent(System::String^ relativePath, bool isDirectory);

    public delegate StatusCode GvNotifyPreRenameEvent(System::String^ relativePath, System::String^ destinationPath);
    
    public delegate StatusCode GvNotifyPreSetHardlinkEvent(System::String^ relativePath, System::String^ destinationPath);

    public delegate void GvNotifyFileRenamedEvent(System::String^ relativePath, System::String^ destinationPath, bool isDirectory, unsigned long% notificationMask);

    public delegate void GvNotifyHardlinkCreatedEvent(System::String^ relativePath, System::String^ destinationPath);

    public delegate void GvNotifyFileHandleClosedEvent(
        System::String^ relativePath,
        bool isDirectory,
        bool fileModified, 
        bool fileDeleted);
}