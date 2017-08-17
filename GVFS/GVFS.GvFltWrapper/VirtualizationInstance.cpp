#include "stdafx.h"
#include "GvFltException.h"
#include "VirtualizationInstance.h"
#include "DirectoryEnumerationResultImpl.h"
#include "DirectoryEnumerationFileNamesResult.h"
#include "Utils.h"

using namespace GvFlt;
using namespace Microsoft::Diagnostics::Tracing;
using namespace System;
using namespace System::Collections::Generic;
using namespace System::ComponentModel;
using namespace System::Text;

namespace
{
    const ULONG READ_BUFFER_SIZE = 64 * 1024;
    const ULONG IDEAL_WRITE_BUFFER_SIZE = 64 * 1024;
    const int EPOCH_RESERVED_BYTES = 4;

    ref class VirtualizationManager
    {
    public:
        // Handle to the active VirtualizationInstance.
        // In the future if we support multiple VirtualizationInstances per provider instance, this can be a map
        // of GV_VIRTUALIZATIONINSTANCE_HANDLE to VirtualizationInstance.  Then in each callback the
        // appropriate VirtualizationInstance instance can be found (and the callback is delivered).
        static VirtualizationInstance^ activeInstance = nullptr;
    };    
    
    // GvFlt callback functions that forward the request from GvFlt to the active
    // VirtualizationInstance (VirtualizationManager::activeInstance)
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
    DirectoryEnumerationResult^ CreateEnumerationResult(
        _In_  FILE_INFORMATION_CLASS fileInformationClass, 
        _In_  PVOID buffer, 
        _In_  ULONG bufferLength,
        _Out_ size_t& fileInfoSize);

    void SetNextEntryOffset(
        _In_ FILE_INFORMATION_CLASS fileInformationClass,
        _In_ PVOID buffer,
        _In_ ULONG offset);

    size_t GetRequiredAlignment(_In_ FILE_INFORMATION_CLASS fileInformationClass);

    array<Byte>^ MarshalPlaceholderId(UCHAR* sourceId);

    void CopyPlaceholderId(UCHAR* destinationId, array<Byte>^ contentId);

    bool IsPowerOf2(ULONG num);

    std::shared_ptr<GV_PLACEHOLDER_INFORMATION> CreatePlaceholderInformation(
        System::DateTime creationTime,
        System::DateTime lastAccessTime,
        System::DateTime lastWriteTime,
        System::DateTime changeTime,
        unsigned long fileAttributes,
        long long endOfFile,
        bool directory,
        array<System::Byte>^ contentId,
        array<System::Byte>^ epochId);
}

VirtualizationInstance::VirtualizationInstance()
    : virtualizationInstanceHandle(nullptr)
    , virtualRootPath(nullptr)
    , writeBufferSize(0)
    , alignmentRequirement(0)
{    
}

StartDirectoryEnumerationEvent^ VirtualizationInstance::OnStartDirectoryEnumeration::get(void)
{
    return this->startDirectoryEnumerationEvent;
}

void VirtualizationInstance::OnStartDirectoryEnumeration::set(StartDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->startDirectoryEnumerationEvent = eventCB;
}

EndDirectoryEnumerationEvent^ VirtualizationInstance::OnEndDirectoryEnumeration::get(void)
{
    return this->endDirectoryEnumerationEvent;
}

void VirtualizationInstance::OnEndDirectoryEnumeration::set(EndDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->endDirectoryEnumerationEvent = eventCB;
}

GetDirectoryEnumerationEvent^ VirtualizationInstance::OnGetDirectoryEnumeration::get(void)
{
    return this->getDirectoryEnumerationEvent;
}

void VirtualizationInstance::OnGetDirectoryEnumeration::set(GetDirectoryEnumerationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->getDirectoryEnumerationEvent = eventCB;
}

QueryFileNameEvent^ VirtualizationInstance::OnQueryFileName::get(void)
{
    return this->queryFileNameEvent;
}

void VirtualizationInstance::OnQueryFileName::set(QueryFileNameEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->queryFileNameEvent = eventCB;
}

GetPlaceholderInformationEvent^ VirtualizationInstance::OnGetPlaceholderInformation::get(void)
{
    return this->getPlaceholderInformationEvent;
}

void VirtualizationInstance::OnGetPlaceholderInformation::set(GetPlaceholderInformationEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->getPlaceholderInformationEvent = eventCB;
}

GetFileStreamEvent^ VirtualizationInstance::OnGetFileStream::get(void)
{
    return this->getFileStreamEvent;
}

void VirtualizationInstance::OnGetFileStream::set(GetFileStreamEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->getFileStreamEvent = eventCB;
}

NotifyFirstWriteEvent^ VirtualizationInstance::OnNotifyFirstWrite::get(void)
{
    return this->notifyFirstWriteEvent;
}

void VirtualizationInstance::OnNotifyFirstWrite::set(NotifyFirstWriteEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFirstWriteEvent = eventCB;
}

NotifyFileHandleCreatedEvent^ VirtualizationInstance::OnNotifyFileHandleCreated::get(void)
{
    return this->notifyFileHandleCreatedEvent;
}

void VirtualizationInstance::OnNotifyFileHandleCreated::set(NotifyFileHandleCreatedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFileHandleCreatedEvent = eventCB;
}

NotifyPreDeleteEvent^ VirtualizationInstance::OnNotifyPreDelete::get(void)
{
    return this->notifyPreDeleteEvent;
}

void VirtualizationInstance::OnNotifyPreDelete::set(NotifyPreDeleteEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPreDeleteEvent = eventCB;
}

NotifyPreRenameEvent^ VirtualizationInstance::OnNotifyPreRename::get(void)
{
    return this->notifyPreRenameEvent;
}

void VirtualizationInstance::OnNotifyPreRename::set(NotifyPreRenameEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPreRenameEvent = eventCB;
}

NotifyPreSetHardlinkEvent^ VirtualizationInstance::OnNotifyPreSetHardlink::get(void)
{
    return this->notifyPreSetHardlinkEvent;
}

void VirtualizationInstance::OnNotifyPreSetHardlink::set(NotifyPreSetHardlinkEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPreSetHardlinkEvent = eventCB;
}

NotifyFileRenamedEvent^ VirtualizationInstance::OnNotifyFileRenamed::get(void)
{
    return this->notifyFileRenamedEvent;
}

void VirtualizationInstance::OnNotifyFileRenamed::set(NotifyFileRenamedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFileRenamedEvent = eventCB;
}

NotifyHardlinkCreatedEvent^ VirtualizationInstance::OnNotifyHardlinkCreated::get(void)
{
    return this->notifyHardlinkCreatedEvent;
}

void VirtualizationInstance::OnNotifyHardlinkCreated::set(NotifyHardlinkCreatedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyHardlinkCreatedEvent = eventCB;
}

NotifyFileHandleClosedEvent^ VirtualizationInstance::OnNotifyFileHandleClosed::get(void)
{
    return this->notifyFileHandleClosedEvent;
}

void VirtualizationInstance::OnNotifyFileHandleClosed::set(NotifyFileHandleClosedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFileHandleClosedEvent = eventCB;
}

ITracer^ VirtualizationInstance::Tracer::get(void)
{
    return this->tracer;
}

HResult VirtualizationInstance::StartVirtualizationInstance(
    ITracer^ tracerImpl,
    System::String^ virtualizationRootPath,
    unsigned long poolThreadCount,
    unsigned long concurrentThreadCount)
{
    this->ConfirmNotStarted();

    if (virtualizationRootPath == nullptr)
    {
        throw gcnew ArgumentNullException(gcnew String("virtualizationRootPath"));
    }

    VirtualizationManager::activeInstance = this;

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

HResult VirtualizationInstance::StopVirtualizationInstance()
{
    long result = ::GvStopVirtualizationInstance(this->virtualizationInstanceHandle);
    if (result == STATUS_SUCCESS)
    {
        this->tracer = nullptr;
        this->virtualizationInstanceHandle = nullptr;
        VirtualizationManager::activeInstance = nullptr;
    }

    return static_cast<HResult>(result);
}

HResult VirtualizationInstance::DetachDriver()
{
    pin_ptr<const WCHAR> rootPath = PtrToStringChars(this->virtualRootPath);
    return static_cast<HResult>(::GvDetachDriver(rootPath));
}


NtStatus VirtualizationInstance::WriteFile(
    Guid streamGuid,
    WriteBuffer^ buffer,
    unsigned long long byteOffset,
    unsigned long length
    )
{
    if (buffer == nullptr)
    {
        return NtStatus::InvalidParameter;
    }

    array<Byte>^ guidData = streamGuid.ToByteArray();
    pin_ptr<Byte> data = &(guidData[0]);
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);

    return static_cast<NtStatus>(::GvWriteFile(
        *instanceHandle,
        *(GUID*)data,
        buffer->Pointer.ToPointer(),
        byteOffset,
        length
        ));
}

NtStatus VirtualizationInstance::DeleteFile(System::String^ relativePath, UpdateType updateFlags, UpdateFailureCause% failureReason)
{
    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    ULONG deleteFailureReason = 0;
    NtStatus result = static_cast<NtStatus>(::GvDeleteFile(*instanceHandle, path, static_cast<ULONG>(updateFlags), &deleteFailureReason));
    failureReason = static_cast<UpdateFailureCause>(deleteFailureReason);
    return result;
}

NtStatus VirtualizationInstance::WritePlaceholderInformation(
    String^ relativePath,
    DateTime creationTime,
    DateTime lastAccessTime,
    DateTime lastWriteTime,
    DateTime changeTime,
    unsigned long fileAttributes,
    long long endOfFile,
    bool directory,
    array<System::Byte>^ contentId,
    array<System::Byte>^ epochId)
{
    if (relativePath == nullptr)
    {
        return NtStatus::InvalidParameter;
    }

    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
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

    return static_cast<NtStatus>(::GvWritePlaceholderInformation(
        *instanceHandle, 
        path, 
        fileInformation.get(), 
        FIELD_OFFSET(GV_PLACEHOLDER_INFORMATION, VariableData))); // We have written no variable data
}

NtStatus VirtualizationInstance::CreatePlaceholderAsHardlink(
    System::String^ destinationFileName,
    System::String^ hardLinkTarget)
{
    if (destinationFileName == nullptr || hardLinkTarget == nullptr)
    {
        return NtStatus::InvalidParameter;
    }

    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    pin_ptr<const WCHAR> targetPath = PtrToStringChars(destinationFileName);
    pin_ptr<const WCHAR> hardLinkPath = PtrToStringChars(hardLinkTarget);

    return static_cast<NtStatus>(::GvCreatePlaceholderAsHardlink(
        *instanceHandle,
        targetPath,
        hardLinkPath));
}

NtStatus VirtualizationInstance::UpdatePlaceholderIfNeeded(
    System::String^ relativePath,
    System::DateTime creationTime,
    System::DateTime lastAccessTime,
    System::DateTime lastWriteTime,
    System::DateTime changeTime,
    unsigned long fileAttributes,
    long long endOfFile,
    array<System::Byte>^ contentId,
    array<System::Byte>^ epochId,
    UpdateType updateFlags, 
    UpdateFailureCause% failureReason)
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
    NtStatus result = static_cast<NtStatus>(::GvUpdatePlaceholderIfNeeded(
        *instanceHandle,
        path,
        fileInformation.get(),
        FIELD_OFFSET(GV_PLACEHOLDER_INFORMATION, VariableData), // We have written no variable data
        static_cast<ULONG>(updateFlags),
        &updateFailureReason));
    failureReason = static_cast<UpdateFailureCause>(updateFailureReason);
    return result;
}

VirtualizationInstance::OnDiskStatus VirtualizationInstance::GetFileOnDiskStatus(System::String^ relativePath)
{
    GUID handleGUID;
    pin_ptr<const WCHAR> filePath = PtrToStringChars(relativePath);
    NTSTATUS openResult = ::GvOpenFile(this->virtualizationInstanceHandle, filePath, GENERIC_READ, &handleGUID);
    if (NT_SUCCESS(openResult))
    {
        NTSTATUS closeResult = ::GvCloseFile(this->virtualizationInstanceHandle, handleGUID);

        if (!NT_SUCCESS(closeResult))
        {
            this->Tracer->TraceError(String::Format("FileExists: GvCloseFile failed for {0}: {1}", relativePath, static_cast<NtStatus>(closeResult)));
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
        throw gcnew GvFltException("ReadFileContents: GvOpenFile failed", static_cast<NtStatus>(openResult));
        break;
    }    
}

System::String^ VirtualizationInstance::ReadFullFileContents(System::String^ relativePath)
{
    GUID handleGUID;
    pin_ptr<const WCHAR> filePath = PtrToStringChars(relativePath);
    NTSTATUS openResult = ::GvOpenFile(this->virtualizationInstanceHandle, filePath, GENERIC_READ, &handleGUID);

    if (!NT_SUCCESS(openResult))
    {
        throw gcnew GvFltException("ReadFileContents: GvOpenFile failed", static_cast<NtStatus>(openResult));
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
                this->Tracer->TraceError(String::Format("ReadFileContents: GvCloseFile failed while closing file after failed read of {0}: {1}", relativePath, static_cast<NtStatus>(closeResult)));
            }

            throw gcnew GvFltException("ReadFileContents: GvReadFile failed", static_cast<NtStatus>(readResult));
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
        this->Tracer->TraceError(String::Format("ReadFileContents: GvCloseFile failed for {0}: {1}", relativePath, static_cast<NtStatus>(closeResult)));
    }

    return allLines->ToString();
}

ULONG VirtualizationInstance::GetWriteBufferSize()
{
    return this->writeBufferSize;
}

ULONG VirtualizationInstance::GetAlignmentRequirement()
{
    return this->alignmentRequirement;
}

WriteBuffer^ VirtualizationInstance::CreateWriteBuffer()
{
    return gcnew WriteBuffer(
        VirtualizationManager::activeInstance->GetWriteBufferSize(),
        VirtualizationManager::activeInstance->GetAlignmentRequirement());
}

//static 
HResult VirtualizationInstance::ConvertDirectoryToVirtualizationRoot(System::Guid virtualizationInstanceGuid, System::String^ rootPath)
{
    array<Byte>^ guidArray = virtualizationInstanceGuid.ToByteArray();
    pin_ptr<Byte> guidData = &(guidArray[0]);

    pin_ptr<const WCHAR> root = PtrToStringChars(rootPath);

    GV_PLACEHOLDER_VERSION_INFO versionInfo;
    memset(&versionInfo, 0, sizeof(GV_PLACEHOLDER_VERSION_INFO));

    return static_cast<HResult>(::GvConvertDirectoryToPlaceholder(
        root,                        // RootPathName
        L"",                         // TargetPathName
        &versionInfo,                // VersionInfo
        0,                           // ReparseTag
        GV_FLAG_VIRTUALIZATION_ROOT, // Flags
        *(GUID*)guidData));          // VirtualizationInstanceID
}

void VirtualizationInstance::ConfirmNotStarted()
{
    if (this->virtualizationInstanceHandle)
    {
        throw gcnew GvFltException("Operation invalid after virtualization instance is started");
    }
}

void VirtualizationInstance::CalculateWriteBufferSizeAndAlignment()
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
        Dictionary<System::String^, System::Object^>^ metadata = gcnew Dictionary<System::String^, System::Object^>();
        metadata->Add("ErrorMessage", "Failed to determine alignment");
        metadata->Add("LogicalBytesPerSector", sectorInfo.LogicalBytesPerSector);
        metadata->Add("writeBufferSize", this->writeBufferSize);
        metadata->Add("alignmentRequirement", this->alignmentRequirement);
        this->tracer->TraceError(metadata);

        CloseHandle(rootHandle);
        FreeLibrary(ntdll);
        throw gcnew GvFltException(String::Format("Failed to determine volume alignment requirement"));
    }

    Dictionary<System::String^, System::Object^>^ metadata = gcnew Dictionary<System::String^, System::Object^>();
    metadata->Add("LogicalBytesPerSector", sectorInfo.LogicalBytesPerSector);
    metadata->Add("writeBufferSize", this -> writeBufferSize);
    metadata->Add("alignmentRequirement", this->alignmentRequirement);
    this->tracer->TraceEvent(EventLevel::Informational, "CalculateWriteBufferSizeAndAlignment", metadata);

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

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnStartDirectoryEnumeration != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnStartDirectoryEnumeration(
                    GUIDtoGuid(enumerationId),
                    gcnew String(pathName)));
            }
            catch (GvFltException^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvStartDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvStartDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvStartDirectoryEnumerationCB fatal exception: " + error->ToString());
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

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnEndDirectoryEnumeration != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnEndDirectoryEnumeration(GUIDtoGuid(enumerationId)));
            }
            catch (GvFltException^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvEndDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvEndDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvEndDirectoryEnumerationCB fatal exception: " + error->ToString());
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

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnGetDirectoryEnumeration != nullptr)
        {            
            memset(fileInformation, 0, *length);
            NTSTATUS resultStatus = STATUS_SUCCESS;
            ULONG totalBytesWritten = 0;

            try
            {
                PVOID outputBuffer = fileInformation;
                DirectoryEnumerationResult^ enumerationData = CreateEnumerationResult(fileInformationClass, outputBuffer, *length, fileInfoSize);
                NtStatus callbackResult = VirtualizationManager::activeInstance->OnGetDirectoryEnumeration(
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

                    while (callbackResult == NtStatus::Succcess && nextEntry != nullptr)
                    {
                        requestedMultipleEntries = true;

                        enumerationData = CreateEnumerationResult(fileInformationClass, nextEntry, static_cast<ULONG>(remainingSpace), fileInfoSize);

                        callbackResult = VirtualizationManager::activeInstance->OnGetDirectoryEnumeration(
                            GUIDtoGuid(enumerationId),
                            filterFileName != NULL ? gcnew String(filterFileName) : nullptr,
                            false, // restartScan
                            enumerationData);

                        if (callbackResult == NtStatus::Succcess)
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
                        if (callbackResult == NtStatus::BufferOverflow)
                        {
                            // We attempted to place multiple entries in the buffer, but not all of them fit, return StatusSucccess
                            // On the next call to GvGetDirectoryEnumerationCB we'll start with the entry that was too
                            // big to fit
                            callbackResult = NtStatus::Succcess;
                        }
                        else if (callbackResult == NtStatus::NoMoreFiles)
                        {
                            // We succeeded in placing all remaining entries in the buffer.  Return StatusSucccess to indicate
                            // that there are entries in the buffer.  On the next call to GvGetDirectoryEnumerationCB StatusNoMoreFiles
                            // will be returned
                            callbackResult = NtStatus::Succcess;
                        }
                    }
                }

                resultStatus = static_cast<NTSTATUS>(callbackResult);
            }
            catch (GvFltException^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetDirectoryEnumerationCB caught GvFltException: " + error->ToString());
                resultStatus = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetDirectoryEnumerationCB caught Win32Exception: " + error->ToString());
                resultStatus = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetDirectoryEnumerationCB fatal exception: " + error->ToString());
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
        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnQueryFileName != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnQueryFileName(gcnew String(pathFileName)));
            }
            catch (GvFltException^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvQueryFileNameCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvQueryFileNameCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvQueryFileNameCB fatal exception: " + error->ToString());
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

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnGetPlaceholderInformation != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            try
            {
                result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnGetPlaceholderInformation(
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
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetPlaceholderInformationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetPlaceholderInformationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetPlaceholderInformationCB fatal exception: " + error->ToString());
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

        if (VirtualizationManager::activeInstance != nullptr)
        {
            if (versionInfo == NULL)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvGetFileStreamCB called with null versionInfo, path: " + gcnew String(pathFileName));
                return static_cast<long>(NtStatus::InternalError);
            }

            if (VirtualizationManager::activeInstance->OnGetFileStream != nullptr)
            {
                NTSTATUS result = STATUS_SUCCESS;
                try
                {
                    result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnGetFileStream(
                        gcnew String(pathFileName),
                        byteOffset.QuadPart,
                        length,
                        GUIDtoGuid(streamGuid),
                        MarshalPlaceholderId(versionInfo->ContentID),
                        MarshalPlaceholderId(versionInfo->EpochID),
                        triggeringProcessId,
                        triggeringProcessImageFileName != nullptr ? gcnew String(triggeringProcessImageFileName) : System::String::Empty));
                }
                catch (GvFltException^ error)
                {
                    switch (error->ErrorCode)
                    {
                    case NtStatus::FileClosed:
                        // StatusFileClosed is expected, and occurs when an application closes a file handle before OnGetFileStream
                        // is complete
                        break;

                    case NtStatus::ObjectNameNotFound:
                        // GvWriteFile may return STATUS_OBJECT_NAME_NOT_FOUND if the stream guid provided is not valid (doesn’t exist in the stream table).
                        // For each file expansion, GVFlt creates a new get stream session with a new stream guid, the session starts at the beginning of the 
                        // file expansion, and ends after the GetFileStream command returns or times out.
                        //
                        // If we hit this in the provider, the most common explanation is that the provider is calling GvWriteFile after the GVFlt thread 
                        // waiting on the respose from GetFileStream has already timed out
                        break;

                    default:
                        VirtualizationManager::activeInstance->Tracer->TraceError("GvGetFileStreamCB caught GvFltException: " + error->ToString());
                        break;
                    }

                    result = static_cast<long>(error->ErrorCode);
                }
                catch (Win32Exception^ error)
                {
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvGetFileStreamCB caught Win32Exception: " + error->ToString());
                    result = Win32ErrorToNtStatus(error->NativeErrorCode);
                }
                catch (Exception^ error)
                {
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvGetFileStreamCB fatal exception: " + error->ToString());
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

        if (VirtualizationManager::activeInstance != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            if (VirtualizationManager::activeInstance->OnNotifyFirstWrite != nullptr)
            {
                try
                {
                    result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnNotifyFirstWrite(gcnew String(pathFileName)));
                }
                catch (GvFltException^ error)
                {
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyFirstWriteCB caught GvFltException: " + error->ToString());
                    result = static_cast<long>(error->ErrorCode);
                }
                catch (Win32Exception^ error)
                {
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyFirstWriteCB caught Win32Exception: " + error->ToString());
                    result = Win32ErrorToNtStatus(error->NativeErrorCode);
                }
                catch (Exception^ error)
                {
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyFirstWriteCB fatal exception: " + error->ToString());
                    throw;
                }
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

        if (VirtualizationManager::activeInstance != nullptr)
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
                    if (VirtualizationManager::activeInstance->OnNotifyFileHandleCreated != nullptr)
                    {
                        VirtualizationManager::activeInstance->OnNotifyFileHandleCreated(
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
                    if (VirtualizationManager::activeInstance->OnNotifyPreDelete != nullptr)
                    {
                        result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnNotifyPreDelete(gcnew String(pathFileName), isDirectory != FALSE));
                    }
                    break;

                case GV_NOTIFICATION_PRE_RENAME:
                    if (VirtualizationManager::activeInstance->OnNotifyPreRename != nullptr)
                    {
                        result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnNotifyPreRename(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName)));
                    }
                    break;

                case GV_NOTIFICATION_PRE_SET_HARDLINK:
                    if (VirtualizationManager::activeInstance->OnNotifyPreSetHardlink != nullptr)
                    {
                        result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnNotifyPreSetHardlink(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName)));
                    }
                    break;

                case GV_NOTIFICATION_FILE_RENAMED:
                    if (VirtualizationManager::activeInstance->OnNotifyFileRenamed != nullptr)
                    {
                        VirtualizationManager::activeInstance->OnNotifyFileRenamed(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName),
                            isDirectory != FALSE,
                            operationParameters->FileRenamed.NotificationMask);
                    }
                    break;

                case GV_NOTIFICATION_HARDLINK_CREATED:
                    if (VirtualizationManager::activeInstance->OnNotifyHardlinkCreated != nullptr)
                    {
                        VirtualizationManager::activeInstance->OnNotifyHardlinkCreated(
                            gcnew String(pathFileName),
                            gcnew String(destinationFileName));
                    }
                    break;
                    
                case GV_NOTIFICATION_FILE_HANDLE_CLOSED:
                    if (VirtualizationManager::activeInstance->OnNotifyFileHandleClosed != nullptr)
                    {
                        VirtualizationManager::activeInstance->OnNotifyFileHandleClosed(
                            gcnew String(pathFileName),
                            isDirectory != FALSE,
                            (operationParameters->HandleClosed.FileModified != FALSE),
                            (operationParameters->HandleClosed.FileDeleted != FALSE));
                    }
                    break;

                default:
                    VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyOperationCB unexpected notification type: " + gcnew String(std::to_string(notificationType).c_str()));
                    break;
                }
            }
            catch (GvFltException^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyOperationCB caught GvFltException: " + error->ToString());
                result = static_cast<long>(error->ErrorCode);
            }
            catch (Win32Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyOperationCB caught Win32Exception: " + error->ToString());
                result = Win32ErrorToNtStatus(error->NativeErrorCode);
            }
            catch (Exception^ error)
            {
                VirtualizationManager::activeInstance->Tracer->TraceError("GvNotifyOperationCB fatal exception: " + error->ToString());
                throw;
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    inline DirectoryEnumerationResult^ CreateEnumerationResult(
        _In_  FILE_INFORMATION_CLASS fileInformationClass,
        _In_  PVOID buffer,
        _In_  ULONG bufferLength,
        _Out_ size_t& fileInfoSize)
    {
        switch (fileInformationClass)
        {
        case FileNamesInformation:
            fileInfoSize = FIELD_OFFSET(FILE_NAMES_INFORMATION, FileName);
            return gcnew DirectoryEnumerationFileNamesResult(static_cast<FILE_NAMES_INFORMATION*>(buffer), bufferLength);
        case FileIdExtdDirectoryInformation:
            fileInfoSize = FIELD_OFFSET(FILE_ID_EXTD_DIR_INFORMATION, FileName);
            return gcnew DirectoryEnumerationResultImpl<FILE_ID_EXTD_DIR_INFORMATION>(static_cast<FILE_ID_EXTD_DIR_INFORMATION*>(buffer), bufferLength);
        case FileIdExtdBothDirectoryInformation:
            fileInfoSize = FIELD_OFFSET(FILE_ID_EXTD_BOTH_DIR_INFORMATION, FileName);
            return gcnew DirectoryEnumerationResultImpl<FILE_ID_EXTD_BOTH_DIR_INFORMATION >(static_cast<FILE_ID_EXTD_BOTH_DIR_INFORMATION *>(buffer), bufferLength);
        default:
            throw gcnew GvFltException(NtStatus::InvalidDeviceRequest);
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
            throw gcnew GvFltException(NtStatus::InvalidDeviceRequest);;
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
            throw gcnew GvFltException(NtStatus::InvalidDeviceRequest);;
        }
    }

    inline array<Byte>^ MarshalPlaceholderId(UCHAR* sourceId)
    {
        array<Byte>^ marshalledId =  gcnew array<Byte>(GV_PLACEHOLDER_ID_LENGTH);
        pin_ptr<byte> pinnedId = &marshalledId[0];
        memcpy(pinnedId, sourceId, GV_PLACEHOLDER_ID_LENGTH);
        return marshalledId;
    }

    inline void CopyPlaceholderId(UCHAR* destinationId, array<Byte>^ sourceId)
    {
        if (sourceId != nullptr && sourceId->Length > 0)
        {
            pin_ptr<byte> pinnedId = &sourceId[0];
            memcpy(
                destinationId,
                pinnedId,
                min(sourceId->Length * sizeof(byte), GV_PLACEHOLDER_ID_LENGTH));
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
        array<System::Byte>^ contentId,
        array<System::Byte>^ epochId)
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
        CopyPlaceholderId(fileInformation->VersionInfo.EpochID, epochId);
        CopyPlaceholderId(fileInformation->VersionInfo.ContentID, contentId);

        return fileInformation;
    }
}