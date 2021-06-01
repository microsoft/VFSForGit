#include "stdafx.h"
#include "NtFunctions.h"
#include "Should.h"

namespace
{
    PQueryDirectoryFile ntQueryDirectoryFile;
    PNtCreateFile ntCreateFile;
    PNtClose ntClose;
    PNtWriteFile ntWriteFile;
    PRtlInitUnicodeString rtlInitUnicodeString;
}

NTSTATUS NtQueryDirectoryFile(
    _In_     HANDLE                 FileHandle,
    _In_opt_ HANDLE                 Event,
    _In_opt_ PIO_APC_ROUTINE        ApcRoutine,
    _In_opt_ PVOID                  ApcContext,
    _Out_    PIO_STATUS_BLOCK       IoStatusBlock,
    _Out_    PVOID                  FileInformation,
    _In_     ULONG                  Length,
    _In_     FILE_INFORMATION_CLASS FileInformationClass,
    _In_     BOOLEAN                ReturnSingleEntry,
    _In_opt_ PUNICODE_STRING        FileName,
    _In_     BOOLEAN                RestartScan
)
{
    if (ntQueryDirectoryFile == NULL)
    {
        HMODULE ntdll = LoadLibrary("ntdll.dll");
        SHOULD_NOT_EQUAL(ntdll, NULL);

        ntQueryDirectoryFile = (PQueryDirectoryFile)GetProcAddress(ntdll, "NtQueryDirectoryFile");
        SHOULD_NOT_EQUAL(ntQueryDirectoryFile, NULL);
    }

    return ntQueryDirectoryFile(
        FileHandle,
        Event,
        ApcRoutine,
        ApcContext,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass,
        ReturnSingleEntry,
        FileName,
        RestartScan);
}

NTSTATUS NtCreateFile(
    _Out_    PHANDLE            FileHandle,
    _In_     ACCESS_MASK        DesiredAccess,
    _In_     POBJECT_ATTRIBUTES ObjectAttributes,
    _Out_    PIO_STATUS_BLOCK   IoStatusBlock,
    _In_opt_ PLARGE_INTEGER     AllocationSize,
    _In_     ULONG              FileAttributes,
    _In_     ULONG              ShareAccess,
    _In_     ULONG              CreateDisposition,
    _In_     ULONG              CreateOptions,
    _In_     PVOID              EaBuffer,
    _In_     ULONG              EaLength
)
{
    if (ntCreateFile == NULL)
    {
        HMODULE ntdll = LoadLibrary("ntdll.dll");
        SHOULD_NOT_EQUAL(ntdll, NULL);

        ntCreateFile = (PNtCreateFile)GetProcAddress(ntdll, "NtCreateFile");
        SHOULD_NOT_EQUAL(ntCreateFile, NULL);
    }

    return ntCreateFile(
        FileHandle,
        DesiredAccess,
        ObjectAttributes,
        IoStatusBlock,
        AllocationSize,
        FileAttributes,
        ShareAccess,
        CreateDisposition,
        CreateOptions,
        EaBuffer,
        EaLength);
}

NTSTATUS NtClose(
    _In_ HANDLE Handle
)
{
    if (ntClose == NULL)
    {
        HMODULE ntdll = LoadLibrary("ntdll.dll");
        SHOULD_NOT_EQUAL(ntdll, NULL);

        ntClose = (PNtClose)GetProcAddress(ntdll, "NtClose");
        SHOULD_NOT_EQUAL(ntClose, NULL);
    }

    return ntClose(Handle);
}

NTSTATUS NtWriteFile(
    _In_     HANDLE           FileHandle,
    _In_opt_ HANDLE           Event,
    _In_opt_ PIO_APC_ROUTINE  ApcRoutine,
    _In_opt_ PVOID            ApcContext,
    _Out_    PIO_STATUS_BLOCK IoStatusBlock,
    _In_     PVOID            Buffer,
    _In_     ULONG            Length,
    _In_opt_ PLARGE_INTEGER   ByteOffset,
    _In_opt_ PULONG           Key
)
{
    if (ntWriteFile == NULL)
    {
        HMODULE ntdll = LoadLibrary("ntdll.dll");
        SHOULD_NOT_EQUAL(ntdll, NULL);

        ntWriteFile = (PNtWriteFile)GetProcAddress(ntdll, "NtWriteFile");
        SHOULD_NOT_EQUAL(ntWriteFile, NULL);
    }

    return ntWriteFile(
        FileHandle,
        Event,
        ApcRoutine,
        ApcContext,
        IoStatusBlock,
        Buffer,
        Length,
        ByteOffset,
        Key);
}

VOID WINAPI RtlInitUnicodeString(
    _Inout_  PUNICODE_STRING DestinationString,
    _In_opt_ PCWSTR          SourceString
)
{
    if (rtlInitUnicodeString == NULL)
    {
        HMODULE ntdll = LoadLibrary("ntdll.dll");
        SHOULD_NOT_EQUAL(ntdll, NULL);

        rtlInitUnicodeString = (PRtlInitUnicodeString)GetProcAddress(ntdll, "RtlInitUnicodeString");
        SHOULD_NOT_EQUAL(rtlInitUnicodeString, NULL);
    }

    return rtlInitUnicodeString(DestinationString, SourceString);
}
