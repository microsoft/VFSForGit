#pragma once

#include "GvFltCallbackDelegates.h"
#include "GvUpdateFailureCause.h"
#include "HResult.h"

namespace GVFSGvFltWrapper 
{
    public ref class GvFltWrapper
    {
    public:
        GvFltWrapper();

        property GvStartDirectoryEnumerationEvent^ OnStartDirectoryEnumeration
        {
            GvStartDirectoryEnumerationEvent^ get(void);
            void set(GvStartDirectoryEnumerationEvent^ eventCB);
        };

        property GvEndDirectoryEnumerationEvent^ OnEndDirectoryEnumeration
        {
            GvEndDirectoryEnumerationEvent^ get(void);
            void set(GvEndDirectoryEnumerationEvent^ eventCB);
        };

        property GvGetDirectoryEnumerationEvent^ OnGetDirectoryEnumeration
        {
            GvGetDirectoryEnumerationEvent^ get(void);
            void set(GvGetDirectoryEnumerationEvent^ eventCB);
        };

        property GvQueryFileNameEvent^ OnQueryFileName
        {
            GvQueryFileNameEvent^ get(void);
            void set(GvQueryFileNameEvent^ eventCB);
        }

        property GvGetPlaceHolderInformationEvent^ OnGetPlaceHolderInformation
        {
            GvGetPlaceHolderInformationEvent^ get(void);
            void set(GvGetPlaceHolderInformationEvent^ eventCB);
        };

        property GvGetFileStreamEvent^ OnGetFileStream
        {
            GvGetFileStreamEvent^ get(void);
            void set(GvGetFileStreamEvent^ eventCB);
        };

        property GvNotifyFirstWriteEvent^ OnNotifyFirstWrite
        {
            GvNotifyFirstWriteEvent^ get(void);
            void set(GvNotifyFirstWriteEvent^ eventCB);
        };

        property GvNotifyCreateEvent^ OnNotifyCreate
        {
            GvNotifyCreateEvent^ get(void);
            void set(GvNotifyCreateEvent^ eventCB);
        };

        property GvNotifyPreDeleteEvent^ OnNotifyPreDelete
        {
            GvNotifyPreDeleteEvent^ get(void);
            void set(GvNotifyPreDeleteEvent^ eventCB);
        }

        property GvNotifyPreRenameEvent^ OnNotifyPreRename
        {
            GvNotifyPreRenameEvent^ get(void);
            void set(GvNotifyPreRenameEvent^ eventCB);
        }

        property GvNotifyPreSetHardlinkEvent^ OnNotifyPreSetHardlink
        {
            GvNotifyPreSetHardlinkEvent^ get(void);
            void set(GvNotifyPreSetHardlinkEvent^ eventCB);
        }

        property GvNotifyFileRenamedEvent^ OnNotifyFileRenamed
        {
            GvNotifyFileRenamedEvent^ get(void);
            void set(GvNotifyFileRenamedEvent^ eventCB);
        }

        property GvNotifyHardlinkCreatedEvent^ OnNotifyHardlinkCreated
        {
            GvNotifyHardlinkCreatedEvent^ get(void);
            void set(GvNotifyHardlinkCreatedEvent^ eventCB);
        }

        property GvNotifyFileHandleClosedEvent^ OnNotifyFileHandleClosed
        {
            GvNotifyFileHandleClosedEvent^ get(void);
            void set(GvNotifyFileHandleClosedEvent^ eventCB);
        }

        property GVFS::Common::Tracing::ITracer^ Tracer
        {
            GVFS::Common::Tracing::ITracer^ get(void);
        };
        
        HResult GvStartVirtualizationInstance(
            GVFS::Common::Tracing::ITracer^ tracerImpl,
            System::String^ virtualizationRootPath,
            unsigned long poolThreadCount,
            unsigned long concurrentThreadCount);

        HResult GvStopVirtualizationInstance();

        HResult GvDetachDriver();

        StatusCode GvWriteFile(
            System::Guid streamGuid,
            GVFltWriteBuffer^ targetBuffer,
            unsigned long long byteOffset,
            unsigned long length);

        StatusCode GvDeleteFile(System::String^ relativePath, GvUpdateType updateFlags, GvUpdateFailureCause% failureReason);

        StatusCode GvWritePlaceholderInformation(
            System::String^ targetRelPathName,
            System::DateTime creationTime,
            System::DateTime lastAccessTime,
            System::DateTime lastWriteTime,
            System::DateTime changeTime,
            unsigned long fileAttributes,
            long long endOfFile,
            bool directory,
            System::String^ contentId,
            System::String^ epochId);

        StatusCode GvCreatePlaceholderAsHardlink(
            System::String^ destinationFileName,
            System::String^ hardLinkTarget);

        StatusCode GvUpdatePlaceholderIfNeeded(
            System::String^ relativePath,
            System::DateTime creationTime,
            System::DateTime lastAccessTime,
            System::DateTime lastWriteTime,
            System::DateTime changeTime,
            unsigned long fileAttributes,
            long long endOfFile,
            System::String^ contentId,
            System::String^ epochId,
            GvUpdateType updateFlags,
            GvUpdateFailureCause% failureReason);

        enum class OnDiskStatus : long
        {
            NotOnDisk = 0,
            Partial = 1,
            Full = 2,
            OnDiskCannotOpen = 3
        };

        // FileExists
        // 
        // Returns:
        //     OnDiskStatus indicating if the file is not on disk, a partial file, or a full file.
        //
        // Throws: 
        //     GvFltException
        //
        // Notes:
        //     This function cannot be used to determine if a folder is partial or full, and cannot be
        //     used to determine if a path is a file or a folder.
        OnDiskStatus GetFileOnDiskStatus(System::String^ relativePath);

        // ReadFullFileContents
        // 
        // Returns:
        //     Contents of the specified full file.  BOM, if present, is not removed.
        //
        // Throws: 
        //     - GvFltException
        System::String^ ReadFullFileContents(System::String^ relativePath);

        ULONG GetWriteBufferSize();
        ULONG GetAlignmentRequirement();

        static HResult GvConvertDirectoryToVirtualizationRoot(System::Guid virtualizationInstanceGuid, System::String^ rootPathName);

    private:
        void ConfirmNotStarted();
        void CalculateWriteBufferSizeAndAlignment();

        GvStartDirectoryEnumerationEvent^ gvStartDirectoryEnumerationEvent;
        GvEndDirectoryEnumerationEvent^ gvEndDirectoryEnumerationEvent;
        GvGetDirectoryEnumerationEvent^ gvGetDirectoryEnumerationEvent;
        GvQueryFileNameEvent^ gvQueryFileNameEvent;
        GvGetPlaceHolderInformationEvent^ gvGetPlaceHolderInformationEvent;
        GvGetFileStreamEvent^ gvGetFileStreamEvent;
        GvNotifyFirstWriteEvent^ gvNotifyFirstWriteEvent;
        GvNotifyCreateEvent^ gvNotifyCreateEvent;
        GvNotifyPreDeleteEvent^ gvNotifyPreDeleteEvent;
        GvNotifyPreRenameEvent^ gvNotifyPreRenameEvent;
        GvNotifyPreSetHardlinkEvent^ gvNotifyPreSetHardlinkEvent;
        GvNotifyFileRenamedEvent^ gvNotifyFileRenamedEvent;
        GvNotifyHardlinkCreatedEvent^ gvNotifyHardlinkCreatedEvent;
        GvNotifyFileHandleClosedEvent^ gvNotifyFileHandleClosedEvent;

        ULONG writeBufferSize;
        ULONG alignmentRequirement;

        GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle;
        System::String^ virtualRootPath;
        GVFS::Common::Tracing::ITracer^ tracer;
    };
}
