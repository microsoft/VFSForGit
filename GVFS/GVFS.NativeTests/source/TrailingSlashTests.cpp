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
    void VerifyPathEnumerationMatches(const std::string& path1, const std::vector<std::wstring>& expectedContents);
}

bool EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete(const char* virtualRootPath)
{
    try
    {
        std::string testFolder = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\EnumerateWithTrailingSlashMatchesWithoutSlashAfterDelete");

        // Folder contains "a.txt", "b.txt", and "c.txt"
        std::vector<std::wstring> expectedResults = { L".", L"..", L"a.txt", L"b.txt", L"c.txt" };
        VerifyPathEnumerationMatches(testFolder, expectedResults);
        VerifyPathEnumerationMatches(testFolder + "\\", expectedResults);

        // Delete a file
        DWORD error = DelFile(testFolder + "\\b.txt");
        SHOULD_EQUAL((DWORD)ERROR_SUCCESS, error);

        expectedResults = { L".", L"..", L"a.txt", L"c.txt" };
        VerifyPathEnumerationMatches(testFolder, expectedResults);
        VerifyPathEnumerationMatches(testFolder + "\\", expectedResults);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

namespace
{
void VerifyPathEnumerationMatches(const std::string& path1, const std::vector<std::wstring>& expectedContents)
{
    SafeHandle folderHandle(CreateFile(
        path1.c_str(),                           // lpFileName
        (GENERIC_READ),                          // dwDesiredAccess
        FILE_SHARE_READ,                         // dwShareMode
        NULL,                                    // lpSecurityAttributes
        OPEN_EXISTING,                           // dwCreationDisposition
        FILE_FLAG_BACKUP_SEMANTICS,              // dwFlagsAndAttributes
        NULL));                                  // hTemplateFile
    
    VerifyEnumerationMatches(folderHandle.GetHandle(), expectedContents);
}

}