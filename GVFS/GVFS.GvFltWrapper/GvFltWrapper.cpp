#include "stdafx.h"
#include "GvFltException.h"
#include "GvFltWrapper.h"
#include "GvDirectoryEnumerationResultImpl.h"
#include "GvDirectoryEnumerationFileNamesResult.h"
#include "Utils.h"

using namespace GVFS::Common::Tracing;
using namespace GVFSGvFltWrapper;
using namespace Microsoft::Diagnostics::Tracing;
using namespace System;
using namespace System::ComponentModel;
using namespace System::Text;

namespace
{
    const ULONG READ_BUFFER_SIZE = 64 * 1024;
    const ULONG IDEAL_WRITE_BUFFER_SIZE = 64 * 1024;
    const UCHAR CURRENT_PLACEHOLDER_VERSION = 1;
    const int EPOCH_RESERVED_BYTES = 4;

    ref class ActiveGvFltManager
    {
    public:
        // Handle to the active GvFltWrapper instance.
        // In the future if we support multiple GvFltWrappers per GVFS instance, this can be a map
        // of GV_VIRTUALIZATIONINSTANCE_HANDLE to GvFltWrapper.  Then in each callback the
        // appropriate GvFltWrapper instance can be found (and the callback is delivered).
        static GvFltWrapper^ activeGvFltWrapper;
    };    
    
    // GvFlt callback functions that forward the request from GvFlt to the active
    // GvFltWrapper (ActiveGvFltManager::activeGvFltWrapper)
    NTSTATUS GvStartDirectoryEnumerationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ GUID                               enumerationId,
        _In_ LPCWSTR                            pathName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       versionInfo);
    
    NTSTATUS GvEndDirectoryEnumerationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_ GUID                              enumerationId);
    
    NTSTATUS GvGetDirectoryEnumerationCB(
        _In_     GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_     GUID                              enumerationId,
        _In_     FILE_INFORMATION_CLASS            fileInformationClass,
        _Inout_  PULONG                            length,
        _In_     LPCWSTR                           filterFileName,
        _In_     BOOLEAN                           returnSingleEntry,
        _In_     BOOLEAN                           restartScan,
        _Out_    PVOID                             fileInformation);

    NTSTATUS GvQueryFileNameCB(
        _In_      GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_      LPCWSTR                            pathFileName
    );

    NTSTATUS GvGetPlaceholderInformationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ LPCWSTR                            pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       parentDirectoryVersionInfo,
        _In_ DWORD                              desiredAccess,
        _In_ DWORD                              shareMode,
        _In_ DWORD                              createDisposition,
        _In_ DWORD                              createOptions,
        _In_ LPCWSTR                            destinationFileName,
        _In_ DWORD                              triggeringProcessId,
        _In_ LPCWSTR                            triggeringProcessImageFileName
    );

    NTSTATUS GvGetFileStreamCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_ LPCWSTR                           pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO      versionInfo,
        _In_ LARGE_INTEGER                     byteOffset,
        _In_ DWORD                             length,
        _In_ ULONG                             flags,
        _In_ GUID                              streamGuid,
        _In_ DWORD                             triggeringProcessId,
        _In_ LPCWSTR                           triggeringProcessImageFileName
    );

    NTSTATUS GvNotifyFirstWriteCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ LPCWSTR                            pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       versionInfo
    );

    NTSTATUS GvNotifyOperationCB(
        _In_     GV_VIRTUALIZATIONINSTANCE_HANDLE virtualizationInstanceHandle,
        _In_     LPCWSTR                          pathFileName,
        _In_     BOOLEAN                          isDirectory,
        _In_     PGV_PLACEHOLDER_VERSION_INFO     versionInfo,
        _In_     GUID                             streamGuid,
        _In_     GUID                             handleGuid,
        _In_     GV_NOTIFICATION_TYPE             notificationType,
        _In_opt_ LPCWSTR                          destinationFileName,
        _Inout_  PGV_OPERATION_PARAMETERS         operationParameters
    );

    // Internal helper functions used by the above callbacks
    GvDirectoryEnumerationResult^ CreateEnumerationResult(
        _In_  FILE_INFORMATION_CLASS fileInformationClass, 
        _In_  PVOID buffer, 
        _In_  ULONG bufferLength,
        _Out_ size_t& fileInfoSize);

    void SetNextEntryOffset(
        _In_ FILE_INFORMATION_CLASS fileInformationClass,
        _In_ PVOID buffer,
        _In_ ULONG offset);

    size_t GetRequiredAlignment(_In_ FILE_INFORMATION_CLASS fileInformationClass);

    UCHAR GetPlaceHolderVersion(const GV_PLACEHOLDER_VERSION_INFO& versionInfo);
    void SetPlaceHolderVersion(GV_PLACEHOLDER_VERSION_INFO& versionInfo, UCHAR version);

    String^ GetContentId(const GV_PLACEHOLDER_VERSION_INFO& versionInfo);
    void SetContentId(GV_PLACEHOLDER_VERSION_INFO& versionInfo, String^ contentId);

    void SetEpochId(GV_PLACEHOLDER_VERSION_INFO& versionInfo, String^ epochId);

    bool IsPowerOf2(ULONG num);

    std::shared_ptr<GV_PLACEHOLDER_INFORMATION> CreatePlaceholderInformation(
        System::DateTime creationTime,
        System::DateTime lastAccessTime,
        System::DateTime lastWriteTime,
        System::DateTime changeTime,
        unsigned long fileAttributes,
        long long endOfFile,
        bool directory,
        System::String^ contentId,
        System::String^ epochId);
}

GvFltWrapper::GvFltWrapper()
    : virtualizationInstanceHandle(nullptr)
    , virtualRootPath(nullptr)
    , writeBufferSize(0)
    , alignmentRequirement(0)
{    
}

GvStartDirectoryEnumerationEvent^ GvFltWrapper::OnStartDirectoryEnumeration::get(void)
{
    return this->gvStartDirectoryEnumerationEvent;
}

void GvFltWrapper::OnStartDirectoryEnumeration::set(GvStartDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvStartDirectoryEnumerationEvent = eventCB;
}

GvEndDirectoryEnumerationEvent^ GvFltWrapper::OnEndDirectoryEnumeration::get(void)
{
    return this->gvEndDirectoryEnumerationEvent;
}

void GvFltWrapper::OnEndDirectoryEnumeration::set(GvEndDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvEndDirectoryEnumerationEvent = eventCB;
}

GvGetDirectoryEnumerationEvent^ GvFltWrapper::OnGetDirectoryEnumeration::get(void)
{
    return this->gvGetDirectoryEnumerationEvent;
}

void GvFltWrapper::OnGetDirectoryEnumeration::set(GvGetDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvGetDirectoryEnumerationEvent = eventCB;
}

GvQueryFileNameEvent^ GvFltWrapper::OnQueryFileName::get(void)
{
    return this->gvQueryFileNameEvent;
}

void GvFltWrapper::OnQueryFileName::set(GvQueryFileNameEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvQueryFileNameEvent = eventCB;
}

GvGetPlaceHolderInformationEvent^ GvFltWrapper::OnGetPlaceHolderInformation::get(void)
{
    return this->gvGetPlaceHolderInformationEvent;
}

void GvFltWrapper::OnGetPlaceHolderInformation::set(GvGetPlaceHolderInformationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvGetPlaceHolderInformationEvent = eventCB;
}

GvGetFileStreamEvent^ GvFltWrapper::OnGetFileStream::get(void)
{
    return this->gvGetFileStreamEvent;
}

void GvFltWrapper::OnGetFileStream::set(GvGetFileStreamEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvGetFileStreamEvent = eventCB;
}

GvNotifyFirstWriteEvent^ GvFltWrapper::OnNotifyFirstWrite::get(void)
{
    return this->gvNotifyFirstWriteEvent;
}

void GvFltWrapper::OnNotifyFirstWrite::set(GvNotifyFirstWriteEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyFirstWriteEvent = eventCB;
}

GvNotifyCreateEvent^ GvFltWrapper::OnNotifyCreate::get(void)
{
    return this->gvNotifyCreateEvent;
}

void GvFltWrapper::OnNotifyCreate::set(GvNotifyCreateEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyCreateEvent = eventCB;
}

GvNotifyPreDeleteEvent^ GvFltWrapper::OnNotifyPreDelete::get(void)
{
    return this->gvNotifyPreDeleteEvent;
}

void GvFltWrapper::OnNotifyPreDelete::set(GvNotifyPreDeleteEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyPreDeleteEvent = eventCB;
}

GvNotifyPreRenameEvent^ GvFltWrapper::OnNotifyPreRename::get(void)
{
    return this->gvNotifyPreRenameEvent;
}

void GvFltWrapper::OnNotifyPreRename::set(GvNotifyPreRenameEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyPreRenameEvent = eventCB;
}

GvNotifyPreSetHardlinkEvent^ GvFltWrapper::OnNotifyPreSetHardlink::get(void)
{
    return this->gvNotifyPreSetHardlinkEvent;
}

void GvFltWrapper::OnNotifyPreSetHardlink::set(GvNotifyPreSetHardlinkEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyPreSetHardlinkEvent = eventCB;
}

GvNotifyFileRenamedEvent^ GvFltWrapper::OnNotifyFileRenamed::get(void)
{
    return this->gvNotifyFileRenamedEvent;
}

void GvFltWrapper::OnNotifyFileRenamed::set(GvNotifyFileRenamedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyFileRenamedEvent = eventCB;
}

GvNotifyHardlinkCreatedEvent^ GvFltWrapper::OnNotifyHardlinkCreated::get(void)
{
    return this->gvNotifyHardlinkCreatedEvent;
}

void GvFltWrapper::OnNotifyHardlinkCreated::set(GvNotifyHardlinkCreatedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyHardlinkCreatedEvent = eventCB;
}

GvNotifyFileHandleClosedEvent^ GvFltWrapper::OnNotifyFileHandleClosed::get(void)
{
    return this->gvNotifyFileHandleClosedEvent;
}

void GvFltWrapper::OnNotifyFileHandleClosed::set(GvNotifyFileHandleClosedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->gvNotifyFileHandleClosedEvent = eventCB;
}

GVFS::Common::Tracing::ITracer^ GvFltWrapper::Tracer::get(void)
{
    return this->tracer;
}

HResult GvFltWrapper::GvStartVirtualizationInstance(
    GVFS::Common::Tracing::ITracer^ tracerImpl,
    System::String^ virtualizationRootPath,
    unsigned long poolThreadCount,
    unsigned long concurrentThreadCount)
{
    this->ConfirmNotStarted();

    if (virtualizationRootPath == nullptr)
    {
        throw gcnew ArgumentNullException(gcnew String("virtualizationRootPath"));
    }

    ActiveGvFltManager::activeGvFltWrapper = this;

    this->tracer = tracerImpl;
    this->virtualRootPath = virtualizationRootPath;

    this->CalculateWriteBufferSizeAndAlignment();

    pin_ptr<const WCHAR> rootPath = PtrToStringChars(this->virtualRootPath);
    GV_COMMAND_CALLBACKS callbacks;
    GvCommandCallbacksInit(&callbacks);
    callbacks.GvStartDirectoryEnumeration = GvStartDirectoryEnumerationCB;
    callbacks.GvEndDirectoryEnumeration = GvEndDirectoryEnumerationCB;
    callbacks.GvGetDirectoryEnumeration = GvGetDirectoryEnumerationCB;
    callbacks.GvQueryFileName = GvQueryFileNameCB;
    callbacks.GvGetPlaceholderInformation = GvGetPlaceholderInformationCB;
    callbacks.GvGetFileStream = GvGetFileStreamCB;
    callbacks.GvNotifyFirstWrite = GvNotifyFirstWriteCB;
    callbacks.GvNotifyOperation = GvNotifyOperationCB;

    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    return static_cast<HResult>(::GvStartVirtualizationInstance(
        rootPath,
        &callbacks,
        0, // flags
        poolThreadCount,
        concurrentThreadCount,
        instanceHandle
        ));
}

HResult GvFltWrapper::GvStopVirtualizationInstance()
{
    long result = ::GvStopVirtualizationInstance(this->virtualizationInstanceHandle);
    if (result == STATUS_SUCCESS)
    {
        this->tracer = nullptr;
        this->virtualizationInstanceHandle = nullptr;
        ActiveGvFltManager::activeGvFltWrapper = nullptr;
    }

    return static_cast<HResult>(result);
}

HResult GvFltWrapper::GvDetachDriver()
{
    pin_ptr<const WCHAR> rootPath = PtrToStringChars(this->virtualRootPath);
    return static_cast<HResult>(::GvDetachDriver(rootPath));
}


StatusCode GvFltWrapper::GvWriteFile(
    Guid streamGuid,
    GVFltWriteBuffer^ targetBuffer,
    unsigned long long byteOffset,
    unsigned long length
    )
{
    array<Byte>^ guidData = streamGuid.ToByteArray();
    pin_ptr<Byte> data = &(guidData[0]);
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);

    return static_cast<StatusCode>(::GvWriteFile(
        *instanceHandle,
        *(GUID*)data,
        targetBuffer->Pointer.ToPointer(),
        byteOffset,
        length
        ));
}

StatusCode GvFltWrapper::GvDeleteFile(System::String^ relativePath, GvUpdateType updateFlags, GvUpdateFailureCause% failureReason)
{
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    ULONG deleteFailureReason = 0;
    StatusCode result = static_cast<StatusCode>(::GvDeleteFile(*instanceHandle, path, static_cast<ULONG>(updateFlags), &deleteFailureReason));
    failureReason = static_cast<GvUpdateFailureCause>(deleteFailureReason);
    return result;
}

StatusCode GvFltWrapper::GvWritePlaceholderInformation(
    String^ targetRelPathName,
    DateTime creationTime,
    DateTime lastAccessTime,
    DateTime lastWriteTime,
    DateTime changeTime,
    unsigned long fileAttributes,
    long long endOfFile,
    bool directory,
    String^ contentId,
    String^ epochId)
{
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> path = PtrToStringChars(targetRelPathName);	
    std::shared_ptr<GV_PLACEHOLDER_INFORMATION> fileInformation = CreatePlaceholderInformation(
        creationTime,
        lastAccessTime,
        lastWriteTime,
        changeTime,
        fileAttributes,
        endOfFile,
        directory,
        contentId,
        epochId);

    return static_cast<StatusCode>(::GvWritePlaceholderInformation(
        *instanceHandle, 
        path, 
        fileInformation.get(), 
        FIELD_OFFSET(GV_PLACEHOLDER_INFORMATION, VariableData))); // We have written no variable data
}

StatusCode GvFltWrapper::GvCreatePlaceholderAsHardlink(
    System::String^ destinationFileName,
    System::String^ hardLinkTarget)
{
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> targetPath = PtrToStringChars(destinationFileName);
    pin_ptr<const WCHAR> hardLinkPath = PtrToStringChars(hardLinkTarget);

    return static_cast<StatusCode>(::GvCreatePlaceholderAsHardlink(
        *instanceHandle,
        targetPath,
        hardLinkPath));
}

StatusCode GvFltWrapper::GvUpdatePlaceholderIfNeeded(
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
    GvUpdateFailureCause% failureReason)
{
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    std::shared_ptr<GV_PLACEHOLDER_INFORMATION> fileInformation = CreatePlaceholderInformation(
        creationTime,
        lastAccessTime,
        lastWriteTime,
        changeTime,
        fileAttributes,
        endOfFile,
        false, // directory
        contentId,
        epochId);

    ULONG updateFailureReason = 0;
    StatusCode result = static_cast<StatusCode>(::GvUpdatePlaceholderIfNeeded(
        *instanceHandle,
        path,
        fileInformation.get(),
        FIELD_OFFSET(GV_PLACEHOLDER_INFORMATION, VariableData), // We have written no variable data
        static_cast<ULONG>(updateFlags),
        &updateFailureReason));
    failureReason = static_cast<GvUpdateFailureCause>(updateFailureReason);
    return result;
}

GvFltWrapper::OnDiskStatus GvFltWrapper::GetFileOnDiskStatus(System::String^ relativePath)
{
    GUID handleGUID;
    pin_ptr<const WCHAR> filePath = PtrToStringChars(relativePath);
    NTSTATUS openResult = ::GvOpenFile(this->virtualizationInstanceHandle, filePath, GENERIC_READ, &handleGUID);
    if (NT_SUCCESS(openResult))
    {
        NTSTATUS closeResult = ::GvCloseFile(this->virtualizationInstanceHandle, handleGUID);

        if (!NT_SUCCESS(closeResult))
        {
            this->Tracer->RelatedError(String::Format("FileExists: GvCloseFile failed for {0}: {1}", relativePath, static_cast<StatusCode>(closeResult)));
        }

        return OnDiskStatus::Full;
    }

    switch (openResult)
    {
    case STATUS_IO_REPARSE_TAG_NOT_HANDLED:
        return OnDiskStatus::Partial;
	case STATUS_SHARING_VIOLATION:
		return OnDiskStatus::OnDiskCannotOpen;
	case STATUS_OBJECT_NAME_NOT_FOUND:
        return OnDiskStatus::NotOnDisk;
    default:
        throw gcnew GvFltException("ReadFileContents: GvOpenFile failed", static_cast<StatusCode>(openResult));
        break;
    }    
}

System::String^ GvFltWrapper::ReadFullFileContents(System::String^ relativePath)
{
    GUID handleGUID;
    pin_ptr<const WCHAR> filePath = PtrToStringChars(relativePath);
    NTSTATUS openResult = ::GvOpenFile(this->virtualizationInstanceHandle, filePath, GENERIC_READ, &handleGUID);

    if (!NT_SUCCESS(openResult))
    {
        throw gcnew GvFltException("ReadFileContents: GvOpenFile failed", static_cast<StatusCode>(openResult));
    }

    StringBuilder^ allLines = gcnew StringBuilder();
    char buffer[READ_BUFFER_SIZE];
    ULONG length = sizeof(buffer) - sizeof(char); // Leave room for null terminator
    ULONG bytesRead = 0;
    ULONGLONG bytesOffset = 0;

    do
    {
        NTSTATUS readResult = ::GvReadFile(this->virtualizationInstanceHandle, handleGUID, buffer, bytesOffset, length, &bytesRead);

        if (!NT_SUCCESS(readResult))
        {
            NTSTATUS closeResult = ::GvCloseFile(this->virtualizationInstanceHandle, handleGUID);

            if (!NT_SUCCESS(closeResult))
            {
                this->Tracer->RelatedError(String::Format("ReadFileContents: GvCloseFile failed while closing file after failed read of {0}: {1}", relativePath, static_cast<StatusCode>(closeResult)));
            }

            throw gcnew GvFltException("ReadFileContents: GvReadFile failed", static_cast<StatusCode>(readResult));
        }

        if (bytesRead > 0)
        {
            // Add null terminator
            *static_cast<char*>(static_cast<void*>(buffer + bytesRead)) = 0;
            allLines->Append(gcnew String(static_cast<char*>(static_cast<void*>(buffer))));
            bytesOffset += bytesRead;
        }
    } while (bytesRead > 0);
    
    NTSTATUS closeResult = ::GvCloseFile(this->virtualizationInstanceHandle, handleGUID);

    if (!NT_SUCCESS(closeResult))
    {
        this->Tracer->RelatedError(String::Format("ReadFileContents: GvCloseFile failed for {0}: {1}", relativePath, static_cast<StatusCode>(closeResult)));
    }

    return allLines->ToString();
}

ULONG GvFltWrapper::GetWriteBufferSize()
{
    return this->writeBufferSize;
}

ULONG GvFltWrapper::GetAlignmentRequirement()
{
    return this->alignmentRequirement;
}

//static 
HResult GvFltWrapper::GvConvertDirectoryToVirtualizationRoot(System::Guid virtualizationInstanceGuid, System::String^ rootPathName)
{
    array<Byte>^ guidArray = virtualizationInstanceGuid.ToByteArray();
    pin_ptr<Byte> guidData = &(guidArray[0]);

    pin_ptr<const WCHAR> rootPath = PtrToStringChars(rootPathName);

    GV_PLACEHOLDER_VERSION_INFO versionInfo;
    memset(&versionInfo, 0, sizeof(GV_PLACEHOLDER_VERSION_INFO));
    SetPlaceHolderVersion(versionInfo, CURRENT_PLACEHOLDER_VERSION);

    return static_cast<HResult>(::GvConvertDirectoryToPlaceholder(
        rootPath,                    // RootPathName
        L"",                         // TargetPathName
        &versionInfo,                // VersionInfo
        0,                           // ReparseTag
        GV_FLAG_VIRTUALIZATION_ROOT, // Flags
        *(GUID*)guidData));          // VirtualizationInstanceID
}

void GvFltWrapper::ConfirmNotStarted()
{
    if (this->virtualizationInstanceHandle)
    {
        throw gcnew GvFltException("Operation invalid after GvFlt is started");
    }
}

void GvFltWrapper::CalculateWriteBufferSizeAndAlignment()
{
    HMODULE ntdll = LoadLibrary(L"ntdll.dll");
    if (!ntdll)
    {
        DWORD lastError = GetLastError();
        throw gcnew GvFltException(String::Format("Failed to load ntdll.dll, Error: {0}", lastError));
    }

    PQueryVolumeInformationFile ntQueryVolumeInformationFile = (PQueryVolumeInformationFile)GetProcAddress(ntdll, "NtQueryVolumeInformationFile");
    if (!ntQueryVolumeInformationFile)
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to get process address of NtQueryVolumeInformationFile, Error: {0}", lastError));
    }

    // TODO 640838: Support paths longer than MAX_PATH
    WCHAR volumePath[MAX_PATH];
    pin_ptr<const WCHAR> rootPath = PtrToStringChars(this->virtualRootPath);
    BOOL success = GetVolumePathName(
        rootPath,
        volumePath,
        ARRAYSIZE(volumePath));
    if (!success) 
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to get volume path name, Error: {0}", lastError));
    }

    WCHAR volumeName[VOLUME_PATH_LENGTH + 1];
    success = GetVolumeNameForVolumeMountPoint(volumePath, volumeName, ARRAYSIZE(volumeName));
    if (!success) 
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to get volume name for volume mount point, Error: {0}", lastError));
    }

    if (wcslen(volumeName) != VOLUME_PATH_LENGTH || volumeName[VOLUME_PATH_LENGTH - 1] != L'\\')
    {
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Volume name {0} is not in expected format", gcnew String(volumeName)));
    }

    HANDLE rootHandle = CreateFile(
        volumeName,
        0,                          // dwDesiredAccess - If this parameter is zero, the application can query certain metadata such as file, directory, 
                                    // or device attributes without accessing that file or device, even if GENERIC_READ access would have been denied
        0,                          // dwShareMode - Access requests to attributes or extended attributes are not affected by this flag.
        NULL,                       // lpSecurityAttributes
        OPEN_EXISTING,              // dwCreationDisposition
        FILE_FLAG_BACKUP_SEMANTICS, // dwFlagsAndAttributes
        NULL);                      // hTemplateFile
    if (rootHandle == INVALID_HANDLE_VALUE)
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to get handle to {0}, Error: {1}", this->virtualRootPath, lastError));
    }

    FILE_FS_SECTOR_SIZE_INFORMATION sectorInfo;
    memset(&sectorInfo, 0, sizeof(FILE_FS_SECTOR_SIZE_INFORMATION));

    IO_STATUS_BLOCK ioStatus;
    memset(&ioStatus, 0, sizeof(IO_STATUS_BLOCK));

    NTSTATUS status = ntQueryVolumeInformationFile(
        rootHandle,
        &ioStatus,
        &sectorInfo,
        sizeof(FILE_FS_SECTOR_SIZE_INFORMATION),
        FileFsSectorSizeInformation);
    if (!NT_SUCCESS(status))
    {
        CloseHandle(rootHandle);
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to query sector size of volume, Status: {0}", status));
    }

    FILE_ALIGNMENT_INFO alignmentInfo;
    memset(&alignmentInfo, 0, sizeof(FILE_ALIGNMENT_INFO));

    success = GetFileInformationByHandleEx(rootHandle, FileAlignmentInfo, &alignmentInfo, sizeof(FILE_ALIGNMENT_INFO));
    if (!success)
    {
        DWORD lastError = GetLastError();
        CloseHandle(rootHandle);
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to query device alignment, Error: {0}", lastError));
    }

    this->writeBufferSize = (IDEAL_WRITE_BUFFER_SIZE / sectorInfo.LogicalBytesPerSector) * sectorInfo.LogicalBytesPerSector;
    this->writeBufferSize = max(sectorInfo.LogicalBytesPerSector, this->writeBufferSize);

    // AlignmentRequirement returns the required alignment minus 1 
    // https://msdn.microsoft.com/en-us/library/cc232065.aspx
    // https://technet.microsoft.com/en-us/windowsserver/ff547807(v=vs.85)
    this->alignmentRequirement = alignmentInfo.AlignmentRequirement + 1;
    
    if (!IsPowerOf2(this->alignmentRequirement))
    {
        EventMetadata^ metadata = gcnew EventMetadata();
        metadata->Add("ErrorMessage", "Failed to determine alignment");
        metadata->Add("LogicalBytesPerSector", sectorInfo.LogicalBytesPerSector);
        metadata->Add("writeBufferSize", this->writeBufferSize);
        metadata->Add("alignmentRequirement", this->alignmentRequirement);
        this->tracer->RelatedError(metadata);

        CloseHandle(rootHandle);
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to determine volume alignment requirement"));
    }

    EventMetadata^ metadata = gcnew EventMetadata();
    metadata->Add("LogicalBytesPerSector", sectorInfo.LogicalBytesPerSector);
    metadata->Add("writeBufferSize", this -> writeBufferSize);
    metadata->Add("alignmentRequirement", this->alignmentRequirement);

    this->tracer->RelatedEvent(EventLevel::Informational, "CalculateWriteBufferSizeAndAlignment", metadata);

    CloseHandle(rootHandle);
    FreeLibrary(ntdll);
}

namespace
{
    NTSTATUS GvStartDirectoryEnumerationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ GUID                               enumerationId,
        _In_ LPCWSTR                            pathName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       versionInfo
        )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        UNREFERENCED_PARAMETER(versionInfo);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr &&
            ActiveGvFltManager::activeGvFltWrapper->OnStartDirectoryEnumeration != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnStartDirectoryEnumeration(
                    GUIDtoGuid(enumerationId),
                    gcnew String(pathName)));
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvStartDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvStartDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvStartDirectoryEnumerationCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvEndDirectoryEnumerationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_ GUID                              enumerationId
        )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr &&
            ActiveGvFltManager::activeGvFltWrapper->OnEndDirectoryEnumeration != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnEndDirectoryEnumeration(GUIDtoGuid(enumerationId)));
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvEndDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvEndDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvEndDirectoryEnumerationCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetDirectoryEnumerationCB(
        _In_     GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_     GUID                              enumerationId,
        _In_     FILE_INFORMATION_CLASS            fileInformationClass,
        _Inout_  PULONG                            length,
        _In_     LPCWSTR                           filterFileName,
        _In_     BOOLEAN                           returnSingleEntry,
        _In_     BOOLEAN                           restartScan,
        _Out_    PVOID                             fileInformation
        )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);

        size_t fileInfoSize = 0;

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr &&
            ActiveGvFltManager::activeGvFltWrapper->OnGetDirectoryEnumeration != nullptr)
        {            
            memset(fileInformation, 0, *length);
            NTSTATUS resultStatus = STATUS_SUCCESS;
            ULONG totalBytesWritten = 0;

            try
            {
                PVOID outputBuffer = fileInformation;
                GvDirectoryEnumerationResult^ enumerationData = CreateEnumerationResult(fileInformationClass, outputBuffer, *length, fileInfoSize);
                StatusCode callbackResult = ActiveGvFltManager::activeGvFltWrapper->OnGetDirectoryEnumeration(
                    GUIDtoGuid(enumerationId),
                    filterFileName != NULL ? gcnew String(filterFileName) : nullptr,
                    (restartScan != FALSE),
                    enumerationData);

                totalBytesWritten = enumerationData->BytesWritten;

                if (!returnSingleEntry)
                {
                    bool requestedMultipleEntries = false;
                    
                    // Entries must be aligned on the proper boundary (either 8-byte or 4-byte depending on the type)
                    size_t alignment = GetRequiredAlignment(fileInformationClass);
                    size_t remainingSpace = static_cast<size_t>(*length - totalBytesWritten);
                    PVOID previousEntry = outputBuffer;
                    PVOID nextEntry = (PUCHAR)outputBuffer + totalBytesWritten;
                    if (!std::align(alignment, fileInfoSize, nextEntry, remainingSpace))
                    {
                        nextEntry = nullptr;
                    }

                    while (callbackResult == StatusCode::StatusSucccess && nextEntry != nullptr)
                    {
                        requestedMultipleEntries = true;

                        enumerationData = CreateEnumerationResult(fileInformationClass, nextEntry, static_cast<ULONG>(remainingSpace), fileInfoSize);

                        callbackResult = ActiveGvFltManager::activeGvFltWrapper->OnGetDirectoryEnumeration(
                            GUIDtoGuid(enumerationId),
                            filterFileName != NULL ? gcnew String(filterFileName) : nullptr,
                            false, // restartScan
                            enumerationData);

                        if (callbackResult == StatusCode::StatusSucccess)
                        {
                            SetNextEntryOffset(fileInformationClass, previousEntry, static_cast<ULONG>((PUCHAR)nextEntry - (PUCHAR)previousEntry));

                            totalBytesWritten = static_cast<ULONG>((PUCHAR)nextEntry - (PUCHAR)outputBuffer) + enumerationData->BytesWritten;

                            // Advance nextEntry to the next boundary aligned spot in the buffer
                            remainingSpace = static_cast<size_t>(*length - totalBytesWritten);
                            previousEntry = nextEntry;
                            nextEntry = (PUCHAR)outputBuffer + totalBytesWritten;
                            if (!std::align(alignment, fileInfoSize, nextEntry, remainingSpace))
                            {
                                nextEntry = nullptr;
                            }
                        }
                    }

                    if (requestedMultipleEntries) 
                    {
                        if (callbackResult == StatusCode::StatusBufferOverflow)
                        {
                            // We attempted to place multiple entries in the buffer, but not all of them fit, return StatusSucccess
                            // On the next call to GvGetDirectoryEnumerationCB we'll start with the entry that was too
                            // big to fit
                            callbackResult = StatusCode::StatusSucccess;
                        }
                        else if (callbackResult == StatusCode::StatusNoMoreFiles)
                        {
                            // We succeeded in placing all remaining entries in the buffer.  Return StatusSucccess to indicate
                            // that there are entries in the buffer.  On the next call to GvGetDirectoryEnumerationCB StatusNoMoreFiles
                            // will be returned
                            callbackResult = StatusCode::StatusSucccess;
                        }
                    }
                }

                resultStatus = static_cast<NTSTATUS>(callbackResult);
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                resultStatus = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                resultStatus = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetDirectoryEnumerationCB fatal exception: " + error->ToString());
                throw;
            }

            *length = totalBytesWritten;

            return resultStatus;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvQueryFileNameCB(
        _In_      GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_      LPCWSTR                            pathFileName
        )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        if (ActiveGvFltManager::activeGvFltWrapper != nullptr && 
            ActiveGvFltManager::activeGvFltWrapper->OnQueryFileName != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnQueryFileName(gcnew String(pathFileName)));
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvQueryFileNameCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvQueryFileNameCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvQueryFileNameCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetPlaceholderInformationCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ LPCWSTR                            pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       parentDirectoryVersionInfo,
        _In_ DWORD                              desiredAccess,
        _In_ DWORD                              shareMode,
        _In_ DWORD                              createDisposition,
        _In_ DWORD                              createOptions,
        _In_ LPCWSTR                            destinationFileName,
        _In_ DWORD                              triggeringProcessId,
        _In_ LPCWSTR                            triggeringProcessImageFileName
        )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        UNREFERENCED_PARAMETER(parentDirectoryVersionInfo);
        UNREFERENCED_PARAMETER(destinationFileName);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr &&
            ActiveGvFltManager::activeGvFltWrapper->OnGetPlaceHolderInformation != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnGetPlaceHolderInformation(
                    gcnew String(pathFileName),
                    desiredAccess,
                    shareMode,
                    createDisposition,
                    createOptions,
                    triggeringProcessId,
                    triggeringProcessImageFileName != nullptr ? gcnew String(triggeringProcessImageFileName) : System::String::Empty));
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetPlaceholderInformationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetPlaceholderInformationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetPlaceholderInformationCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetFileStreamCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE  virtualizationInstanceHandle,
        _In_ LPCWSTR                           pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO      versionInfo,
        _In_ LARGE_INTEGER                     byteOffset,
        _In_ DWORD                             length,
        _In_ ULONG                             flags,
        _In_ GUID                              streamGuid,
        _In_ DWORD                             triggeringProcessId,
        _In_ LPCWSTR                           triggeringProcessImageFileName
    )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        UNREFERENCED_PARAMETER(flags);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr)
        {
            if (versionInfo == NULL)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetFileStreamCB called with null versionInfo, path: " + gcnew String(pathFileName));
                return static_cast<long>(StatusCode::StatusInternalError);
            }
            else if (GetPlaceHolderVersion(*versionInfo) != CURRENT_PLACEHOLDER_VERSION)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError(
                    "GvGetFileStreamCB: Unexpected placeholder version " + gcnew String(std::to_wstring(GetPlaceHolderVersion(*versionInfo)).c_str()) + " for file " + gcnew String(pathFileName));
                return static_cast<long>(StatusCode::StatusInternalError);
            }

            if (ActiveGvFltManager::activeGvFltWrapper->OnGetFileStream != nullptr)
            {
                NTSTATUS result = STATUS_SUCCESS;
                try
                {
                    GVFltWriteBuffer targetBuffer(
                        ActiveGvFltManager::activeGvFltWrapper->GetWriteBufferSize(), 
                        ActiveGvFltManager::activeGvFltWrapper->GetAlignmentRequirement());

                    result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnGetFileStream(
                        gcnew String(pathFileName),
                        byteOffset.QuadPart,
                        length,
                        GUIDtoGuid(streamGuid),
                        GetContentId(*versionInfo),
                        triggeringProcessId,
                        triggeringProcessImageFileName != nullptr ? gcnew String(triggeringProcessImageFileName) : System::String::Empty,
                        %targetBuffer));
                }
                catch (GvFltException^ error)
                {
                    switch (error->ErrorCode)
                    {
                    case StatusCode::StatusFileClosed:
                        // StatusFileClosed is expected, and occurs when an application closes a file handle before OnGetFileStream
                        // is complete
                        break;

                    case StatusCode::StatusObjectNameNotFound:
                        // GvWriteFile may return STATUS_OBJECT_NAME_NOT_FOUND if the stream guid provided is not valid (doesn’t exist in the stream table).
                        // For each file expansion, GVFlt creates a new get stream session with a new stream guid, the session starts at the beginning of the 
                        // file expansion, and ends after the GetFileStream command returns or times out.
                        //
                        // If we hit this in GVFS, the most common explanation is that we're calling GvWriteFile after the GVFlt thread waiting on the respose
                        // from GetFileStream has already timed out
                        break;

                    default:
                        ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetFileStreamCB caught GvFltException: " + error->ToString());
                        break;
                    }

                    result = static_cast<long>(error->ErrorCode);
                }
                catch (Win32Exception^ error)
                {
                    ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetFileStreamCB caught Win32Exception: " + error->ToString());
                    result = Win32ErrorToNtStatus(error->NativeErrorCode);
                }
                catch (Exception^ error)
                {
                    ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvGetFileStreamCB fatal exception: " + error->ToString());
                    throw;
                }

                return result;
            }
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvNotifyFirstWriteCB(
        _In_ GV_VIRTUALIZATIONINSTANCE_HANDLE   virtualizationInstanceHandle,
        _In_ LPCWSTR                            pathFileName,
        _In_ PGV_PLACEHOLDER_VERSION_INFO       versionInfo
    )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        UNREFERENCED_PARAMETER(versionInfo);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr &&
            ActiveGvFltManager::activeGvFltWrapper->OnNotifyFirstWrite != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnNotifyFirstWrite(gcnew String(pathFileName)));
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyFirstWriteCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyFirstWriteCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyFirstWriteCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvNotifyOperationCB(
        _In_     GV_VIRTUALIZATIONINSTANCE_HANDLE virtualizationInstanceHandle,
        _In_     LPCWSTR                          pathFileName,
        _In_     BOOLEAN                          isDirectory,
        _In_     PGV_PLACEHOLDER_VERSION_INFO     versionInfo,
        _In_     GUID                             streamGuid,
        _In_     GUID                             handleGuid,
        _In_     GV_NOTIFICATION_TYPE             notificationType,
        _In_opt_ LPCWSTR                          destinationFileName,
        _Inout_  PGV_OPERATION_PARAMETERS         operationParameters
    )
    {
        UNREFERENCED_PARAMETER(virtualizationInstanceHandle);
        UNREFERENCED_PARAMETER(versionInfo);
        UNREFERENCED_PARAMETER(streamGuid);
        UNREFERENCED_PARAMETER(handleGuid);

        if (ActiveGvFltManager::activeGvFltWrapper != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;
            try
            {
                // NOTE: Post callbacks have void return type.  The return type is void because
                // they are not allowed to fail as the operation has already taken place.  If, in the
                // future, we were to allow post callbacks to fail the application would need to take
                // all necessary actions to undo the operation that has succeeded.
                switch (notificationType)
                {
                case GV_NOTIFICATION_POST_CREATE:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyCreate != nullptr)
                    {
                        ActiveGvFltManager::activeGvFltWrapper->OnNotifyCreate(
                            gcnew String(pathFileName),
							isDirectory != FALSE,
                            operationParameters->PostCreate.DesiredAccess,
                            operationParameters->PostCreate.ShareMode,
                            operationParameters->PostCreate.CreateDisposition,
                            operationParameters->PostCreate.CreateOptions,
                            static_cast<IoStatusBlockValue>(operationParameters->PostCreate.IoStatusBlock),
                            operationParameters->PostCreate.NotificationMask);
                    }
                    break;

                case GV_NOTIFICATION_PRE_DELETE:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreDelete != nullptr)
                    {
                        result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreDelete(gcnew String(pathFileName), isDirectory != FALSE));
                    }
                    break;

                case GV_NOTIFICATION_PRE_RENAME:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreRename != nullptr)
                    {
                        result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreRename(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName)));
                    }
                    break;

                case GV_NOTIFICATION_PRE_SET_HARDLINK:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreSetHardlink != nullptr)
                    {
                        result = static_cast<NTSTATUS>(ActiveGvFltManager::activeGvFltWrapper->OnNotifyPreSetHardlink(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName)));
                    }
                    break;

                case GV_NOTIFICATION_FILE_RENAMED:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyFileRenamed != nullptr)
                    {
                        ActiveGvFltManager::activeGvFltWrapper->OnNotifyFileRenamed(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName),
                            isDirectory != FALSE,
                            operationParameters->FileRenamed.NotificationMask);
                    }
                    break;

                case GV_NOTIFICATION_HARDLINK_CREATED:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyHardlinkCreated != nullptr)
                    {
                        ActiveGvFltManager::activeGvFltWrapper->OnNotifyHardlinkCreated(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName));
                    }
                    break;
                    
                case GV_NOTIFICATION_FILE_HANDLE_CLOSED:
                    if (ActiveGvFltManager::activeGvFltWrapper->OnNotifyFileHandleClosed != nullptr)
                    {
                        ActiveGvFltManager::activeGvFltWrapper->OnNotifyFileHandleClosed(
                            gcnew String(pathFileName),
                            isDirectory != FALSE,
                            (operationParameters->HandleClosed.FileModified != FALSE),
                            (operationParameters->HandleClosed.FileDeleted != FALSE));
                    }
                    break;

                default:
                    ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyOperationCB unexpected notification type: " + gcnew String(std::to_string(notificationType).c_str()));
                    break;
                }
            }
            catch (GvFltException^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyOperationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyOperationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                ActiveGvFltManager::activeGvFltWrapper->Tracer->RelatedError("GvNotifyOperationCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    inline GvDirectoryEnumerationResult^ CreateEnumerationResult(
        _In_  FILE_INFORMATION_CLASS fileInformationClass,
        _In_  PVOID buffer,
        _In_  ULONG bufferLength,
        _Out_ size_t& fileInfoSize)
    {
        switch (fileInformationClass)
        {
        case FileNamesInformation:
            fileInfoSize = FIELD_OFFSET(FILE_NAMES_INFORMATION, FileName);
            return gcnew GvDirectoryEnumerationFileNamesResult(static_cast<FILE_NAMES_INFORMATION*>(buffer), bufferLength);
        case FileIdExtdDirectoryInformation:
            fileInfoSize = FIELD_OFFSET(FILE_ID_EXTD_DIR_INFORMATION, FileName);
            return gcnew GvDirectoryEnumerationResultImpl<FILE_ID_EXTD_DIR_INFORMATION>(static_cast<FILE_ID_EXTD_DIR_INFORMATION*>(buffer), bufferLength);
        case FileIdExtdBothDirectoryInformation:
            fileInfoSize = FIELD_OFFSET(FILE_ID_EXTD_BOTH_DIR_INFORMATION, FileName);
            return gcnew GvDirectoryEnumerationResultImpl<FILE_ID_EXTD_BOTH_DIR_INFORMATION >(static_cast<FILE_ID_EXTD_BOTH_DIR_INFORMATION *>(buffer), bufferLength);
        default:
            throw gcnew GvFltException(StatusCode::StatusInvalidDeviceRequest);
        }
    }

    inline void SetNextEntryOffset(
        _In_ FILE_INFORMATION_CLASS fileInformationClass,
        _In_ PVOID buffer, 
        _In_ ULONG offset)
    {
        switch (fileInformationClass)
        {
        case FileNamesInformation:
            static_cast<FILE_NAMES_INFORMATION*>(buffer)->NextEntryOffset = offset;
            break;
        case FileIdExtdDirectoryInformation:
            static_cast<FILE_ID_EXTD_DIR_INFORMATION*>(buffer)->NextEntryOffset = offset;
            break;
        case FileIdExtdBothDirectoryInformation:
            static_cast<FILE_ID_EXTD_BOTH_DIR_INFORMATION*>(buffer)->NextEntryOffset = offset;
            break;
        default:
            throw gcnew GvFltException(StatusCode::StatusInvalidDeviceRequest);;
        }
    }

    inline size_t GetRequiredAlignment(_In_ FILE_INFORMATION_CLASS fileInformationClass)
    {
        switch (fileInformationClass)
        {
        case FileIdExtdDirectoryInformation: // Could not find offset for FILE_ID_EXTD_DIR_INFORMATION in MSDN, assuming it is 8 for now
        case FileIdExtdBothDirectoryInformation: 
            return 8;
        case FileNamesInformation:
            return 4;
            break;
        default:
            throw gcnew GvFltException(StatusCode::StatusInvalidDeviceRequest);;
        }
    }

    inline UCHAR GetPlaceHolderVersion(const GV_PLACEHOLDER_VERSION_INFO& versionInfo)
    {
        return versionInfo.EpochID[0];
    }

    inline void SetPlaceHolderVersion(GV_PLACEHOLDER_VERSION_INFO& versionInfo, UCHAR version)
    {
        // Use the first byte of VersionInfo.EpochID to store GVFS's version number for placeholders
        versionInfo.EpochID[0] = version;
    }

    inline String^ GetContentId(const GV_PLACEHOLDER_VERSION_INFO& versionInfo)
    {
        return gcnew String(
            static_cast<wchar_t*>(static_cast<void*>(const_cast<GV_PLACEHOLDER_VERSION_INFO&>(versionInfo).ContentID)));
    }

    inline void SetContentId(GV_PLACEHOLDER_VERSION_INFO& versionInfo, String^ contentId)
    {
        if (contentId->Length > 0)
        {
            pin_ptr<const WCHAR> unmangedContentId = PtrToStringChars(contentId);
            memcpy(
                versionInfo.ContentID,
                unmangedContentId,
                min(contentId->Length * sizeof(WCHAR), GV_PLACEHOLDER_ID_LENGTH - sizeof(WCHAR)));
        }
    }

    inline void SetEpochId(GV_PLACEHOLDER_VERSION_INFO& versionInfo, String^ epochId)
    {
        if (!String::IsNullOrEmpty(epochId))
        {
            pin_ptr<const WCHAR> unmangedEpochId = PtrToStringChars(epochId);
            memcpy(
                versionInfo.EpochID + EPOCH_RESERVED_BYTES,
                unmangedEpochId,
                min(epochId->Length * sizeof(WCHAR), (GV_PLACEHOLDER_ID_LENGTH - sizeof(WCHAR)) - sizeof(UCHAR)));
        }
    }

    inline bool IsPowerOf2(ULONG num)
    {
        return (num & (num - 1)) == 0;
    }

    inline std::shared_ptr<GV_PLACEHOLDER_INFORMATION> CreatePlaceholderInformation(
        System::DateTime creationTime,
        System::DateTime lastAccessTime,
        System::DateTime lastWriteTime,
        System::DateTime changeTime,
        unsigned long fileAttributes,
        long long endOfFile,
        bool directory,
        System::String^ contentId,
        System::String^ epochId)
    {
        std::shared_ptr<GV_PLACEHOLDER_INFORMATION> fileInformation(static_cast<GV_PLACEHOLDER_INFORMATION*>(malloc(sizeof(GV_PLACEHOLDER_INFORMATION))), free);

        memset(&fileInformation->FileBasicInformation, 0, sizeof(FILE_BASIC_INFORMATION));
        memset(&fileInformation->FileStandardInformation, 0, sizeof(FILE_STANDARD_INFORMATION));

        fileInformation->FileBasicInformation.CreationTime.QuadPart = creationTime.ToFileTime();
        fileInformation->FileBasicInformation.LastAccessTime.QuadPart = lastAccessTime.ToFileTime();
        fileInformation->FileBasicInformation.LastWriteTime.QuadPart = lastWriteTime.ToFileTime();
        fileInformation->FileBasicInformation.ChangeTime.QuadPart = changeTime.ToFileTime();
        fileInformation->FileBasicInformation.FileAttributes = fileAttributes;

        // A placeholder is a sparse file and so the file system doesn’t provide it any allocation when it's created on disk
        fileInformation->FileStandardInformation.AllocationSize.QuadPart = 0;
        fileInformation->FileStandardInformation.EndOfFile.QuadPart = endOfFile;
        fileInformation->FileStandardInformation.Directory = directory;

        fileInformation->EaInformation.EaBufferSize = 0;
        fileInformation->EaInformation.OffsetToFirstEa = static_cast<ULONG>(-1);
        fileInformation->SecurityInformation.SecurityBufferSize = 0;
        fileInformation->SecurityInformation.OffsetToSecurityDescriptor = static_cast<ULONG>(-1);
        fileInformation->StreamsInformation.StreamsInfoBufferSize = 0;
        fileInformation->StreamsInformation.OffsetToFirstStreamInfo = static_cast<ULONG>(-1);

        fileInformation->Flags = 0;

        memset(&fileInformation->VersionInfo, 0, sizeof(GV_PLACEHOLDER_VERSION_INFO));
        SetPlaceHolderVersion(fileInformation->VersionInfo, CURRENT_PLACEHOLDER_VERSION);
        SetEpochId(fileInformation->VersionInfo, epochId);
        SetContentId(fileInformation->VersionInfo, contentId);

        return fileInformation;
    }
}