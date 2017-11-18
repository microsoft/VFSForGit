#include "stdafx.h"
#include "GvLibException.h"
#include "VirtualizationInstance.h"
#include "DirectoryEnumerationResultImpl.h"
#include "DirectoryEnumerationFileNamesResult.h"
#include "Utils.h"

using namespace GvLib;
using namespace System;
using namespace System::Globalization;

namespace
{
    ref class VirtualizationManager
    {
    public:        
        // TODO 1064209: 
        //     - Support multiple VirtualizationInstances per provider instance
        //     - Make accessing  any static data in VirtualizationManager thread safe

        // Handle to the active VirtualizationInstance.
        static VirtualizationInstance^ activeInstance = nullptr;
    };    
    
    // GvLib callback functions that forward the request from GvLib to the active
    // VirtualizationInstance (VirtualizationManager::activeInstance)
    NTSTATUS GvStartDirectoryEnumerationCB(
        _In_ PGV_CALLBACK_DATA                callbackData,
        _In_ GUID                             enumerationId);
    
    NTSTATUS GvEndDirectoryEnumerationCB(
        _In_ PGV_CALLBACK_DATA                callbackData,
        _In_ GUID                             enumerationId);
    
    NTSTATUS GvGetDirectoryEnumerationCB(
        _In_     PGV_CALLBACK_DATA            callbackData,
        _In_     GUID                         enumerationId,
        _In_     FILE_INFORMATION_CLASS       fileInformationClass,
        _Inout_  PULONG                       length,
        _In_     LPCWSTR                      filterFileName,
        _In_     BOOLEAN                      returnSingleEntry,
        _In_     BOOLEAN                      restartScan,
        _Out_    PVOID                        fileInformation);

    NTSTATUS GvQueryFileNameCB(
        _In_     PGV_CALLBACK_DATA            callbackData);

    NTSTATUS GvGetPlaceholderInformationCB(
        _In_ PGV_CALLBACK_DATA                callbackData,
        _In_ DWORD                            desiredAccess,
        _In_ DWORD                            shareMode,
        _In_ DWORD                            createDisposition,
        _In_ DWORD                            createOptions,
        _In_ LPCWSTR                          destinationFileName);

    NTSTATUS GvGetFileStreamCB(
        _In_ PGV_CALLBACK_DATA                callbackData,
        _In_ LARGE_INTEGER                    byteOffset,
        _In_ DWORD                            length);

    NTSTATUS GvNotifyFirstWriteCB(
        _In_      PGV_CALLBACK_DATA           callbackData);

    NTSTATUS GvNotifyOperationCB(
        _In_     PGV_CALLBACK_DATA            callbackData,
        _In_     BOOLEAN                      isDirectory,
        _In_     GV_NOTIFICATION_TYPE         notificationType,
        _In_opt_ LPCWSTR                      destinationFileName,
        _Inout_  PGV_OPERATION_PARAMETERS     operationParameters);

    void GvCancelCommandCB(
        _In_     PGV_CALLBACK_DATA              callbackData);

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

    System::Guid GUIDtoGuid(const GUID& guid);

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

    // Converts a strongly typed enum to its underlying type
    template <typename T>
    constexpr std::underlying_type_t<T> CastToUnderlyingType(T e) noexcept
    {
        return static_cast<std::underlying_type_t<T>>(e);
    }
}

VirtualizationInstance::VirtualizationInstance()
    : virtualizationInstanceHandle(nullptr)
    , virtualRootPath(nullptr)
    , bytesPerSector(0)
    , writeBufferAlignmentRequirement(0)
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

NotifyPostCreateHandleOnlyEvent^ VirtualizationInstance::OnNotifyPostCreateHandleOnly::get(void)
{
    return this->notifyPostCreateHandleOnlyEvent;
}

void VirtualizationInstance::OnNotifyPostCreateHandleOnly::set(NotifyPostCreateHandleOnlyEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPostCreateHandleOnlyEvent = eventCB;
}

NotifyPostCreateNewFileEvent^ VirtualizationInstance::OnNotifyPostCreateNewFile::get(void)
{
    return this->notifyPostCreateNewFileEvent;
}

void VirtualizationInstance::OnNotifyPostCreateNewFile::set(NotifyPostCreateNewFileEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPostCreateNewFileEvent = eventCB;
}

NotifyPostCreateOverwrittenOrSupersededEvent^ VirtualizationInstance::OnNotifyPostCreateOverwrittenOrSuperseded::get(void)
{
    return this->notifyPostCreateOverwrittenOrSupersededEvent;
}

void VirtualizationInstance::OnNotifyPostCreateOverwrittenOrSuperseded::set(NotifyPostCreateOverwrittenOrSupersededEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyPostCreateOverwrittenOrSupersededEvent = eventCB;
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

NotifyFileHandleClosedOnlyEvent^ VirtualizationInstance::OnNotifyFileHandleClosedOnly::get(void)
{
    return this->notifyFileHandleClosedOnlyEvent;
}

void VirtualizationInstance::OnNotifyFileHandleClosedOnly::set(NotifyFileHandleClosedOnlyEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFileHandleClosedOnlyEvent = eventCB;
}

NotifyFileHandleClosedModifiedOrDeletedEvent^ VirtualizationInstance::OnNotifyFileHandleClosedModifiedOrDeleted::get(void)
{
    return this->notifyFileHandleClosedModifiedOrDeletedEvent;
}

void VirtualizationInstance::OnNotifyFileHandleClosedModifiedOrDeleted::set(NotifyFileHandleClosedModifiedOrDeletedEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->notifyFileHandleClosedModifiedOrDeletedEvent = eventCB;
}

CancelCommandEvent^ VirtualizationInstance::OnCancelCommand::get(void)
{
    return this->cancelCommandEvent;
}

void VirtualizationInstance::OnCancelCommand::set(CancelCommandEvent^ eventCB)
{
    this->ConfirmNotStarted();
    this->cancelCommandEvent = eventCB;
}

HResult VirtualizationInstance::StartVirtualizationInstance(
    System::String^ virtualizationRootPath,
    unsigned long poolThreadCount,
    unsigned long concurrentThreadCount,
    bool enableNegativePathCache,
    NotificationType globalNotificationMask,
    unsigned long% logicalBytesPerSector,
    unsigned long% writeBufferAlignment)
{
    if (virtualizationRootPath == nullptr)
    {
        throw gcnew ArgumentNullException(gcnew String("virtualizationRootPath"));
    }

    if (VirtualizationManager::activeInstance != nullptr && VirtualizationManager::activeInstance != this)
    {
        throw gcnew InvalidOperationException(gcnew String("Only one VirtualizationInstance can be running at a time"));
    }

    VirtualizationManager::activeInstance = this;

    this->virtualRootPath = virtualizationRootPath;

    this->FindBytesPerSectorAndAlignment();
    logicalBytesPerSector = this->bytesPerSector;
    writeBufferAlignment = this->writeBufferAlignmentRequirement;

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
    callbacks.GvCancelCommand = GvCancelCommandCB;

    pin_ptr<GV_VIRTUALIZATIONINSTANCE_HANDLE> instanceHandle = &(this->virtualizationInstanceHandle);
    return static_cast<HResult>(::GvStartVirtualizationInstance(
        rootPath,
        &callbacks,
        enableNegativePathCache ? GV_FLAG_INSTANCE_NEGATIVE_PATH_CACHE : 0,
        CastToUnderlyingType(globalNotificationMask),
        poolThreadCount,
        concurrentThreadCount,
        NULL, // InstanceContext, pointer to context information defined by the provider for each instance
        instanceHandle
        ));
}

HResult VirtualizationInstance::StopVirtualizationInstance()
{
    long result = ::GvStopVirtualizationInstance(this->virtualizationInstanceHandle);
    if (result == STATUS_SUCCESS)
    {
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

NtStatus VirtualizationInstance::ClearNegativePathCache(unsigned long% totalEntryNumber)
{
    ULONG entryCount = 0;
    NtStatus result = static_cast<NtStatus>(::GvClearNegativePathCache(this->virtualizationInstanceHandle, &entryCount));
    totalEntryNumber = entryCount;

    return result;
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
    return static_cast<NtStatus>(::GvWriteFile(
        this->virtualizationInstanceHandle,
        *(GUID*)data,
        buffer->Pointer.ToPointer(),
        byteOffset,
        length
        ));
}

NtStatus VirtualizationInstance::DeleteFile(System::String^ relativePath, UpdateType updateFlags, UpdateFailureCause% failureReason)
{
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    ULONG deleteFailureReason = 0;
    NtStatus result = static_cast<NtStatus>(::GvDeleteFile(this->virtualizationInstanceHandle, path, static_cast<ULONG>(updateFlags), &deleteFailureReason));
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

    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    return static_cast<NtStatus>(::GvWritePlaceholderInformation(
        this->virtualizationInstanceHandle,
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

    pin_ptr<const WCHAR> targetPath = PtrToStringChars(destinationFileName);
    pin_ptr<const WCHAR> hardLinkPath = PtrToStringChars(hardLinkTarget);
    return static_cast<NtStatus>(::GvCreatePlaceholderAsHardlink(
        this->virtualizationInstanceHandle,
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
    pin_ptr<const WCHAR> path = PtrToStringChars(relativePath);
    NtStatus result = static_cast<NtStatus>(::GvUpdatePlaceholderIfNeeded(
        this->virtualizationInstanceHandle,
        path,
        fileInformation.get(),
        FIELD_OFFSET(GV_PLACEHOLDER_INFORMATION, VariableData), // We have written no variable data
        CastToUnderlyingType(updateFlags),
        &updateFailureReason));

    failureReason = static_cast<UpdateFailureCause>(updateFailureReason);
    return result;
}

void VirtualizationInstance::CompleteCommand(
    long commandId,
    NtStatus completionStatus)
{
    ::GvCompleteCommand(
        this->virtualizationInstanceHandle,
        commandId,
        static_cast<NTSTATUS>(completionStatus),
        NULL, // ReplyBuffer
        0);   // ReplyBufferSize
}

WriteBuffer^ VirtualizationInstance::CreateWriteBuffer(unsigned long desiredBufferSize)
{
    ULONG writeBufferSize = desiredBufferSize;
    if (writeBufferSize < this->bytesPerSector)
    {
        writeBufferSize = this->bytesPerSector;
    }
    else
    {
        ULONG bufferRemainder = desiredBufferSize % this->bytesPerSector;
        if (bufferRemainder != 0)
        {
            // Round up to nearest multiple of this->bytesPerSector
            writeBufferSize += (this->bytesPerSector - bufferRemainder);
        }
    }

    return gcnew WriteBuffer(writeBufferSize, this->writeBufferAlignmentRequirement);
}

//static 
HResult VirtualizationInstance::ConvertDirectoryToVirtualizationRoot(System::Guid virtualizationInstanceGuid, System::String^ rootPath)
{
    GV_PLACEHOLDER_VERSION_INFO versionInfo;
    memset(&versionInfo, 0, sizeof(GV_PLACEHOLDER_VERSION_INFO));

    array<Byte>^ guidArray = virtualizationInstanceGuid.ToByteArray();
    pin_ptr<Byte> guidData = &(guidArray[0]);
    pin_ptr<const WCHAR> root = PtrToStringChars(rootPath);
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
        throw gcnew InvalidOperationException("Operation invalid after virtualization instance is started");
    }
}

void VirtualizationInstance::FindBytesPerSectorAndAlignment()
{
    HMODULE ntdll = LoadLibrary(L"ntdll.dll");
    if (!ntdll)
    {
        DWORD lastError = GetLastError();
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to load ntdll.dll, Error: {0}", lastError));
    }

    PQueryVolumeInformationFile ntQueryVolumeInformationFile = (PQueryVolumeInformationFile)GetProcAddress(ntdll, "NtQueryVolumeInformationFile");
    if (!ntQueryVolumeInformationFile)
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to get process address of NtQueryVolumeInformationFile, Error: {0}", lastError));
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
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to get volume path name, Error: {0}", lastError));
    }

    WCHAR volumeName[VOLUME_PATH_LENGTH + 1];
    success = GetVolumeNameForVolumeMountPoint(volumePath, volumeName, ARRAYSIZE(volumeName));
    if (!success) 
    {
        DWORD lastError = GetLastError();
        FreeLibrary(ntdll);
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to get volume name for volume mount point, Error: {0}", lastError));
    }

    if (wcslen(volumeName) != VOLUME_PATH_LENGTH || volumeName[VOLUME_PATH_LENGTH - 1] != L'\\')
    {
        FreeLibrary(ntdll);
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Volume name {0} is not in expected format", gcnew String(volumeName)));
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
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to get handle to {0}, Error: {1}", this->virtualRootPath, lastError));
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
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to query sector size of volume, Status: {0}", status));
    }

    FILE_ALIGNMENT_INFO alignmentInfo;
    memset(&alignmentInfo, 0, sizeof(FILE_ALIGNMENT_INFO));

    success = GetFileInformationByHandleEx(rootHandle, FileAlignmentInfo, &alignmentInfo, sizeof(FILE_ALIGNMENT_INFO));
    if (!success)
    {
        DWORD lastError = GetLastError();
        CloseHandle(rootHandle);
        FreeLibrary(ntdll);
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to query device alignment, Error: {0}", lastError));
    }

    this->bytesPerSector = sectorInfo.LogicalBytesPerSector;

    // AlignmentRequirement returns the required alignment minus 1 
    // https://msdn.microsoft.com/en-us/library/cc232065.aspx
    // https://docs.microsoft.com/en-us/windows-hardware/drivers/kernel/initializing-a-device-object
    this->writeBufferAlignmentRequirement = alignmentInfo.AlignmentRequirement + 1;
    
    CloseHandle(rootHandle);
    FreeLibrary(ntdll);

    if (!IsPowerOf2(this->writeBufferAlignmentRequirement))
    {
        throw gcnew GvLibException(String::Format(CultureInfo::InvariantCulture, "Failed to determine write buffer alignment requirement: {0} is not a power of 2", this->writeBufferAlignmentRequirement));
    }
}

namespace
{
    NTSTATUS GvStartDirectoryEnumerationCB(
        _In_ PGV_CALLBACK_DATA                callbackData,
        _In_ GUID                             enumerationId)
    {
        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnStartDirectoryEnumeration != nullptr)
        {
            return static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnStartDirectoryEnumeration(
                    callbackData->CommandId,
                    GUIDtoGuid(enumerationId),
                    gcnew String(callbackData->FilePathName)));
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvEndDirectoryEnumerationCB(
        _In_ PGV_CALLBACK_DATA                 callbackData,
        _In_ GUID                              enumerationId)
    {
        UNREFERENCED_PARAMETER(callbackData);

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnEndDirectoryEnumeration != nullptr)
        {
            return static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnEndDirectoryEnumeration(GUIDtoGuid(enumerationId)));
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetDirectoryEnumerationCB(
        _In_     PGV_CALLBACK_DATA                 callbackData,
        _In_     GUID                              enumerationId,
        _In_     FILE_INFORMATION_CLASS            fileInformationClass,
        _Inout_  PULONG                            length,
        _In_     LPCWSTR                           filterFileName,
        _In_     BOOLEAN                           returnSingleEntry,
        _In_     BOOLEAN                           restartScan,
        _Out_    PVOID                             fileInformation)
    {
        UNREFERENCED_PARAMETER(callbackData);

        size_t fileInfoSize = 0;

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnGetDirectoryEnumeration != nullptr)
        {            
            memset(fileInformation, 0, *length);
            ULONG totalBytesWritten = 0;

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
                bool bufferContainsAtLeastOneEntry = false;
                    
                // Entries must be aligned on the proper boundary (either 8-byte or 4-byte depending on the type)
                size_t alignment = GetRequiredAlignment(fileInformationClass);
                size_t remainingSpace = static_cast<size_t>(*length - totalBytesWritten);
                PVOID previousEntry = outputBuffer;
                PVOID nextEntry = (PUCHAR)outputBuffer + totalBytesWritten;
                if (!std::align(alignment, fileInfoSize, nextEntry, remainingSpace))
                {
                    nextEntry = nullptr;
                }

                while (callbackResult == NtStatus::Success && nextEntry != nullptr)
                {
                    bufferContainsAtLeastOneEntry = true;

                    enumerationData = CreateEnumerationResult(fileInformationClass, nextEntry, static_cast<ULONG>(remainingSpace), fileInfoSize);

                    callbackResult = VirtualizationManager::activeInstance->OnGetDirectoryEnumeration(
                        GUIDtoGuid(enumerationId),
                        filterFileName != NULL ? gcnew String(filterFileName) : nullptr,
                        false, // restartScan
                        enumerationData);

                    if (callbackResult == NtStatus::Success)
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

                if (bufferContainsAtLeastOneEntry)
                {
                    if (callbackResult == NtStatus::BufferOverflow)
                    {
                        // We attempted to place multiple entries in the buffer, but not all of them fit, return StatusSucccess
                        // On the next call to GvGetDirectoryEnumerationCB we'll start with the entry that was too
                        // big to fit
                        callbackResult = NtStatus::Success;
                    }
                    else if (callbackResult == NtStatus::NoMoreFiles)
                    {
                        // We succeeded in placing all remaining entries in the buffer.  Return StatusSucccess to indicate
                        // that there are entries in the buffer.  On the next call to GvGetDirectoryEnumerationCB StatusNoMoreFiles
                        // will be returned
                        callbackResult = NtStatus::Success;
                    }
                }
            }

            *length = totalBytesWritten;

            return static_cast<NTSTATUS>(callbackResult);
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvQueryFileNameCB(
        _In_     PGV_CALLBACK_DATA                 callbackData)
    {
        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnQueryFileName != nullptr)
        {
            return static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnQueryFileName(gcnew String(callbackData->FilePathName)));
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetPlaceholderInformationCB(
        _In_ PGV_CALLBACK_DATA                  callbackData,
        _In_ DWORD                              desiredAccess,
        _In_ DWORD                              shareMode,
        _In_ DWORD                              createDisposition,
        _In_ DWORD                              createOptions,
        _In_ LPCWSTR                            destinationFileName)
    {
        UNREFERENCED_PARAMETER(destinationFileName);

        if (VirtualizationManager::activeInstance != nullptr &&
            VirtualizationManager::activeInstance->OnGetPlaceholderInformation != nullptr)
        {
            return static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnGetPlaceholderInformation(
                callbackData->CommandId,
                gcnew String(callbackData->FilePathName),
                desiredAccess,
                shareMode,
                createDisposition,
                createOptions,
                callbackData->TriggeringProcessId,
                callbackData->TriggeringProcessImageFileName != NULL ? gcnew String(callbackData->TriggeringProcessImageFileName) : System::String::Empty));
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvGetFileStreamCB(
        _In_ PGV_CALLBACK_DATA                 callbackData,
        _In_ LARGE_INTEGER                     byteOffset,
        _In_ DWORD                             length)
    {
        if (VirtualizationManager::activeInstance != nullptr && VirtualizationManager::activeInstance->OnGetFileStream != nullptr)
        {
            return static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnGetFileStream(
                callbackData->CommandId,
                gcnew String(callbackData->FilePathName),
                byteOffset.QuadPart,
                length,
                GUIDtoGuid(callbackData->StreamGuid),
                callbackData->VersionInfo != NULL ? MarshalPlaceholderId(callbackData->VersionInfo->ContentID) : nullptr,
                callbackData->VersionInfo != NULL ? MarshalPlaceholderId(callbackData->VersionInfo->EpochID) : nullptr,
                callbackData->TriggeringProcessId,
                callbackData->TriggeringProcessImageFileName != NULL ? gcnew String(callbackData->TriggeringProcessImageFileName) : System::String::Empty));
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvNotifyFirstWriteCB(
        _In_      PGV_CALLBACK_DATA                  callbackData)
    {
        if (VirtualizationManager::activeInstance != nullptr)
        {
            NTSTATUS result = STATUS_SUCCESS;

            if (VirtualizationManager::activeInstance->OnNotifyFirstWrite != nullptr)
            {
                result = static_cast<NTSTATUS>(VirtualizationManager::activeInstance->OnNotifyFirstWrite(gcnew String(callbackData->FilePathName)));
            }

            return result;
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS GvNotifyOperationCB(
        _In_     PGV_CALLBACK_DATA              callbackData,
        _In_     BOOLEAN                        isDirectory,
        _In_     GV_NOTIFICATION_TYPE           notificationType,
        _In_opt_ LPCWSTR                        destinationFileName,
        _Inout_  PGV_OPERATION_PARAMETERS       operationParameters)
    {
        if (VirtualizationManager::activeInstance != nullptr)
        {
            NtStatus result = NtStatus::Success;

            // NOTE: Post callbacks have void return type.  The return type is void because
            // they are not allowed to fail as the operation has already taken place.  If, in the
            // future, we were to allow post callbacks to fail the application would need to take
            // all necessary actions to undo the operation that has succeeded.
            switch (notificationType)
            {
            case GV_NOTIFICATION_POST_CREATE_HANDLE_ONLY:
                if (VirtualizationManager::activeInstance->OnNotifyPostCreateHandleOnly != nullptr)
                {
                    NotificationType notificationMask;
                    VirtualizationManager::activeInstance->OnNotifyPostCreateHandleOnly(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE,
                        operationParameters->PostCreate.DesiredAccess,
                        operationParameters->PostCreate.ShareMode,
                        operationParameters->PostCreate.CreateDisposition,
                        operationParameters->PostCreate.CreateOptions,
                        static_cast<IoStatusBlockValue>(operationParameters->PostCreate.IoStatusBlock),
                        notificationMask);
                    operationParameters->PostCreate.NotificationMask = CastToUnderlyingType(notificationMask);
                }
                break;

            case GV_NOTIFICATION_POST_CREATE_NEW_FILE:
                if (VirtualizationManager::activeInstance->OnNotifyPostCreateNewFile != nullptr)
                {
                    NotificationType notificationMask;
                    VirtualizationManager::activeInstance->OnNotifyPostCreateNewFile(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE,
                        operationParameters->PostCreate.DesiredAccess,
                        operationParameters->PostCreate.ShareMode,
                        operationParameters->PostCreate.CreateDisposition,
                        operationParameters->PostCreate.CreateOptions,
                        notificationMask);
                    operationParameters->PostCreate.NotificationMask = CastToUnderlyingType(notificationMask);
                }
                break;

            case GV_NOTIFICATION_POST_CREATE_OVERWRITTEN_OR_SUPERSEDED:
                if (VirtualizationManager::activeInstance->OnNotifyPostCreateOverwrittenOrSuperseded != nullptr)
                {
                    NotificationType notificationMask;
                    VirtualizationManager::activeInstance->OnNotifyPostCreateOverwrittenOrSuperseded(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE,
                        operationParameters->PostCreate.DesiredAccess,
                        operationParameters->PostCreate.ShareMode,
                        operationParameters->PostCreate.CreateDisposition,
                        operationParameters->PostCreate.CreateOptions,
                        static_cast<IoStatusBlockValue>(operationParameters->PostCreate.IoStatusBlock),
                        notificationMask);
                    operationParameters->PostCreate.NotificationMask = CastToUnderlyingType(notificationMask);
                }
                break;

            case GV_NOTIFICATION_PRE_DELETE:
                if (VirtualizationManager::activeInstance->OnNotifyPreDelete != nullptr)
                {
                    result = VirtualizationManager::activeInstance->OnNotifyPreDelete(gcnew String(callbackData->FilePathName), isDirectory != FALSE);
                }
                break;

            case GV_NOTIFICATION_PRE_RENAME:
                if (VirtualizationManager::activeInstance->OnNotifyPreRename != nullptr)
                {
                    result = VirtualizationManager::activeInstance->OnNotifyPreRename(
                        gcnew String(callbackData->FilePathName),
                        gcnew String(destinationFileName));
                }
                break;

            case GV_NOTIFICATION_PRE_SET_HARDLINK:
                if (VirtualizationManager::activeInstance->OnNotifyPreSetHardlink != nullptr)
                {
                    result = VirtualizationManager::activeInstance->OnNotifyPreSetHardlink(
                        gcnew String(callbackData->FilePathName),
                        gcnew String(destinationFileName));
                }
                break;

            case GV_NOTIFICATION_FILE_RENAMED:
                if (VirtualizationManager::activeInstance->OnNotifyFileRenamed != nullptr)
                {
                    NotificationType notificationMask;
                    VirtualizationManager::activeInstance->OnNotifyFileRenamed(
                        gcnew String(callbackData->FilePathName),
                        gcnew String(destinationFileName),
                        isDirectory != FALSE,
                        notificationMask);
                    operationParameters->FileRenamed.NotificationMask = CastToUnderlyingType(notificationMask);
                }
                break;

            case GV_NOTIFICATION_HARDLINK_CREATED:
                if (VirtualizationManager::activeInstance->OnNotifyHardlinkCreated != nullptr)
                {
                    VirtualizationManager::activeInstance->OnNotifyHardlinkCreated(
                        gcnew String(callbackData->FilePathName),
                        gcnew String(destinationFileName));
                }
                break;

            case GV_NOTIFICATION_FILE_HANDLE_CLOSED_ONLY:
                if (VirtualizationManager::activeInstance->OnNotifyFileHandleClosedOnly != nullptr)
                {
                    VirtualizationManager::activeInstance->OnNotifyFileHandleClosedOnly(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE);
                }
                break;

            case GV_NOTIFICATION_FILE_HANDLE_CLOSED_MODIFIED:
                if (VirtualizationManager::activeInstance->OnNotifyFileHandleClosedModifiedOrDeleted != nullptr)
                {
                    VirtualizationManager::activeInstance->OnNotifyFileHandleClosedModifiedOrDeleted(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE,
                        true,    // isFileModified
                        false);  // isFileDeleted
                }
                break;

            case GV_NOTIFICATION_FILE_HANDLE_CLOSED_DELETED:
                if (VirtualizationManager::activeInstance->OnNotifyFileHandleClosedModifiedOrDeleted != nullptr)
                {
                    VirtualizationManager::activeInstance->OnNotifyFileHandleClosedModifiedOrDeleted(
                        gcnew String(callbackData->FilePathName),
                        isDirectory != FALSE,
                        operationParameters->FileDeletedOnHandleClose.IsFileModified != FALSE,    // isFileModified
                        true);  // isFileDeleted
                }
                break;

            default:
                // Unexpected notification type
                break;
            }
            
            return static_cast<NTSTATUS>(result);
        }

        return STATUS_INVALID_DEVICE_STATE;
    }

    void GvCancelCommandCB(
        _In_     PGV_CALLBACK_DATA              callbackData)
    {
        if (VirtualizationManager::activeInstance != nullptr && VirtualizationManager::activeInstance->OnCancelCommand != nullptr)
        {
            VirtualizationManager::activeInstance->OnCancelCommand(callbackData->CommandId);
        }
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
            throw gcnew GvLibException(
                String::Format("CreateEnumerationResult: Invalid fileInformationClass: {0}", static_cast<int>(fileInformationClass)), 
                NtStatus::InvalidDeviceRequest);
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
            throw gcnew GvLibException(
                String::Format("SetNextEntryOffset: Invalid fileInformationClass: {0}", static_cast<int>(fileInformationClass)),
                NtStatus::InvalidDeviceRequest);
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
            throw gcnew GvLibException(
                String::Format("GetRequiredAlignment: Invalid fileInformationClass: {0}", static_cast<int>(fileInformationClass)),
                NtStatus::InvalidDeviceRequest);
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

    inline System::Guid GUIDtoGuid(const GUID& guid)
    {
        return System::Guid(
            guid.Data1,
            guid.Data2,
            guid.Data3,
            guid.Data4[0],
            guid.Data4[1],
            guid.Data4[2],
            guid.Data4[3],
            guid.Data4[4],
            guid.Data4[5],
            guid.Data4[6],
            guid.Data4[7]);
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