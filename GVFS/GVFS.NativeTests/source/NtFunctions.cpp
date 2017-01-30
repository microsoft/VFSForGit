#include "stdafx.h"
#include "NtFunctions.h"
#include "Should.h"

namespace
{
    PQueryDirectoryFile ntQueryDirectoryFile;
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