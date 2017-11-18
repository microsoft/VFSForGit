#include "stdafx.h"
#include "TrailingSlashTests.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "Should.h"

using namespace TestHelpers;

namespace
{
    const std::string TEST_ROOT_FOLDER("\\TrailingSlashTests");
    void VerifyEnumerationMatches(const std::string& path1, const std::vector<std::wstring>& expectedContents);
}

bool EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete(const char* virtualRootPath)
{
    try
    {
        std::string testFolder = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete");

        // Folder contains "a.txt", "b.txt", and "c.txt"
        std::vector<std::wstring> expectedResults = { L".", L"..", L"a.txt", L"b.txt", L"c.txt" };
        VerifyEnumerationMatches(testFolder, expectedResults);
        VerifyEnumerationMatches(testFolder + "\\", expectedResults);

        // Delete a file
        DWORD error = DelFile(testFolder + "\\b.txt");
        SHOULD_EQUAL((DWORD)ERROR_SUCCESS, error);

        expectedResults = { L".", L"..", L"a.txt", L"c.txt" };
        VerifyEnumerationMatches(testFolder, expectedResults);
        VerifyEnumerationMatches(testFolder + "\\", expectedResults);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

namespace
{

void VerifyEnumerationMatches(const std::string& path1, const std::vector<std::wstring>& expectedContents)
{
    SafeHandle folderHandle(CreateFile(
        path1.c_str(),                           // lpFileName
        (GENERIC_READ),                          // dwDesiredAccess
        FILE_SHARE_READ,                         // dwShareMode
        NULL,                                    // lpSecurityAttributes
        OPEN_EXISTING,                           // dwCreationDisposition
        FILE_FLAG_BACKUP_SEMANTICS,              // dwFlagsAndAttributes
        NULL));                                  // hTemplateFile
    SHOULD_NOT_EQUAL(folderHandle.GetHandle(), INVALID_HANDLE_VALUE);

    UCHAR buffer[2048];
    NTSTATUS status;
    IO_STATUS_BLOCK ioStatus;
    BOOLEAN restart = TRUE;
    size_t expectedIndex = 0;

    do
    {
        status = NtQueryDirectoryFile(folderHandle.GetHandle(),
            NULL,
            NULL,
            NULL,
            &ioStatus,
            buffer,
            ARRAYSIZE(buffer),
            FileBothDirectoryInformation,
            FALSE,
            NULL,
            restart);

        if (status == STATUS_SUCCESS)
        {
            PFILE_BOTH_DIR_INFORMATION dirInfo;
            PUCHAR entry = buffer;			

            do 
            {
                dirInfo = (PFILE_BOTH_DIR_INFORMATION)entry;

                std::wstring entryName(dirInfo->FileName, dirInfo->FileNameLength / sizeof(WCHAR));

                SHOULD_EQUAL(entryName, expectedContents[expectedIndex]);

                entry = entry + dirInfo->NextEntryOffset;
                ++expectedIndex;

            } while (dirInfo->NextEntryOffset > 0 && expectedIndex < expectedContents.size());

            restart = FALSE;
        }

    } while (status == STATUS_SUCCESS);	

    SHOULD_EQUAL(expectedIndex, expectedContents.size());
    SHOULD_EQUAL(status, STATUS_NO_MORE_FILES);
}

}