#include "stdafx.h"
#include "NtQueryDirectoryFileTests.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "Should.h"

bool QueryDirectoryFileRestartScanResetsFilter(const char* folderPath)
{
    try
    {
        SHOULD_BE_TRUE(PathIsDirectory(folderPath));

        SafeHandle folderHandle(CreateFile(
            folderPath,                              // lpFileName
            (GENERIC_READ),                          // dwDesiredAccess
            FILE_SHARE_READ,                         // dwShareMode
            NULL,                                    // lpSecurityAttributes
            OPEN_EXISTING,                           // dwCreationDisposition
            FILE_FLAG_BACKUP_SEMANTICS,              // dwFlagsAndAttributes
            NULL));                                  // hTemplateFile
        SHOULD_NOT_EQUAL(folderHandle.GetHandle(), INVALID_HANDLE_VALUE);

        IO_STATUS_BLOCK ioStatus;
        FILE_NAMES_INFORMATION namesInfo[64];
        memset(namesInfo, 0, sizeof(namesInfo));

        NTSTATUS status = NtQueryDirectoryFile(
            folderHandle.GetHandle(), // FileHandle
            NULL,                     // Event
            NULL,                     // ApcRoutine
            NULL,                     // ApcContext
            &ioStatus,                // IoStatusBlock
            namesInfo,                // FileInformation
            sizeof(namesInfo),        // Length
            FileNamesInformation,     // FileInformationClass
            FALSE,                    // ReturnSingleEntry
            NULL,                     // FileName
            FALSE);                   // RestartScan

        SHOULD_EQUAL(status, STATUS_SUCCESS);
        memset(namesInfo, 0, sizeof(namesInfo));

        status = NtQueryDirectoryFile(
            folderHandle.GetHandle(), // FileHandle
            NULL,                     // Event
            NULL,                     // ApcRoutine
            NULL,                     // ApcContext
            &ioStatus,                // IoStatusBlock
            namesInfo,                // FileInformation
            sizeof(namesInfo),        // Length
            FileNamesInformation,     // FileInformationClass
            FALSE,                    // ReturnSingleEntry
            NULL,                     // FileName
            TRUE);                    // RestartScan

        SHOULD_EQUAL(status, STATUS_SUCCESS);
        memset(namesInfo, 0, sizeof(namesInfo));

        wchar_t nonExistentFileName[] = L"IDontExist";
        UNICODE_STRING nonExistentFileFilter;
        nonExistentFileFilter.Buffer = nonExistentFileName;
        nonExistentFileFilter.Length = sizeof(nonExistentFileName) - sizeof(wchar_t); // Length should not include null terminator
        nonExistentFileFilter.MaximumLength = sizeof(nonExistentFileName);

        status = NtQueryDirectoryFile(
            folderHandle.GetHandle(), // FileHandle
            NULL,                     // Event
            NULL,                     // ApcRoutine
            NULL,                     // ApcContext
            &ioStatus,                // IoStatusBlock
            namesInfo,                // FileInformation
            sizeof(namesInfo),        // Length
            FileNamesInformation,     // FileInformationClass
            FALSE,                    // ReturnSingleEntry
            &nonExistentFileFilter,   // FileName
            TRUE);                    // RestartScan

        SHOULD_EQUAL(status, STATUS_NO_MORE_FILES);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}
