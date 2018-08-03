#include "stdafx.h"
#include "ProjFS_DirEnumTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "Should.h"

using namespace TestHelpers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_EnumTest");

bool ProjFS_EnumEmptyFolder(const char* virtualRootPath)
{
    try
    {
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumEmptyFolder\\");
        CreateDirectoryWithIntermediates(folderPath);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)2, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumFolderWithOneFileInPackage(const char* virtualRootPath)
{
    try
    {
        std::vector<FileInfo> result = EnumDirectory(virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInPackage\\"));
        VERIFY_ARE_EQUAL((size_t)3, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL("onlyFileInFolder.txt", result[2].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumFolderWithOneFileInBoth(const char* virtualRootPath)
{
    try
    {
        std::string existingFileName = "newfileInPackage.txt";
        std::string newFileName = "newfileInScratch.txt";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInBoth\\");
        CreateNewFile(folderPath + newFileName);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)4, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(existingFileName, result[2].Name);
        VERIFY_ARE_EQUAL(true, result[3].IsFile);
        VERIFY_ARE_EQUAL(newFileName, result[3].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumFolderWithOneFileInBoth1(const char* virtualRootPath)
{
    try
    {
        std::string existingFileName = "newfileInPackage.txt";
        std::string newFileName = "123";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInBoth1\\");
        CreateNewFile(folderPath + newFileName);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)4, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(newFileName, result[2].Name);
        VERIFY_ARE_EQUAL(true, result[3].IsFile);
        VERIFY_ARE_EQUAL(existingFileName, result[3].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumFolderDeleteExistingFile(const char* virtualRootPath)
{
    try
    {
        std::string fileName1 = "fileInPackage1.txt";
        std::string fileName2 = "fileInPackage2.txt";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderDeleteExistingFile\\");

        VERIFY_ARE_EQUAL(TRUE, DeleteFile((folderPath + fileName1).c_str()));

        std::vector<FileInfo>  result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)3, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(fileName2, result[2].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumFolderSmallBuffer(const char* virtualRootPath)
{
    std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderSmallBuffer");

    try
    {
        UCHAR buffer[512];
        NTSTATUS status;
        BOOLEAN restart = TRUE;
        IO_STATUS_BLOCK ioStatus;
        USHORT count = 0;

        for (USHORT i = 0; i < 26; i += 2) {

            // open every other file to create placeholders
            std::string name(1, (char)('a' + i));
            OpenForRead(folderPath + std::string("\\") + name);
        }

        std::shared_ptr<void> handle = OpenForRead(folderPath);

        do 
        {
            status = NtQueryDirectoryFile(handle.get(),
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

            if (status == STATUS_SUCCESS) {

                PFILE_BOTH_DIR_INFORMATION dirInfo;
                PUCHAR entry = buffer;

                do {

                    dirInfo = (PFILE_BOTH_DIR_INFORMATION)entry;

                    std::wstring entryName(dirInfo->FileName, dirInfo->FileNameLength / sizeof(WCHAR));

                    if ((entryName.compare(L".") != 0) && (entryName.compare(L"..") != 0)) {

                        std::wstring expectedName(1, L'a' + count);

                        VERIFY_ARE_EQUAL(entryName, expectedName);

                        count++;
                    }

                    entry = entry + dirInfo->NextEntryOffset;

                } while (dirInfo->NextEntryOffset > 0);

                restart = FALSE;
            }

        } while (status == STATUS_SUCCESS);

        VERIFY_ARE_EQUAL(count, 26);
        VERIFY_ARE_EQUAL(status, STATUS_NO_MORE_FILES);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_EnumTestNoMoreNoSuchReturnCodes(const char* virtualRootPath)
{
    std::string fileName1 = "fileInPackage1.txt";
    std::string fileName2 = "fileInPackage2.txt";
    std::string fileName3 = "fileInPackage3.txt";
    std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumTestNoMoreNoSuchReturnCodes");

    try
    {
        VERIFY_ARE_EQUAL(TRUE, DeleteFile((folderPath + std::string("\\") + fileName1).c_str()));
        VERIFY_ARE_EQUAL(TRUE, DeleteFile((folderPath + std::string("\\") + fileName2).c_str()));
        VERIFY_ARE_EQUAL(TRUE, DeleteFile((folderPath + std::string("\\") + fileName3).c_str()));

        std::shared_ptr<void> enumHandle = OpenForRead(folderPath);

        UCHAR buffer[512];
        BOOLEAN restartScan = TRUE;
        IO_STATUS_BLOCK ioStatus;
        UNICODE_STRING fileSpec;
        RtlInitUnicodeString(&fileSpec, L"fileInPack*");
        NTSTATUS initialStatus = NtQueryDirectoryFile(enumHandle.get(),
            nullptr,
            nullptr,
            nullptr,
            &ioStatus,
            buffer,
            ARRAYSIZE(buffer),
            FileFullDirectoryInformation,
            FALSE,
            &fileSpec,
            restartScan);

        // Check expected status code for the first query on a given handle for a non-existent name.
        VERIFY_ARE_EQUAL(STATUS_NO_SUCH_FILE, initialStatus);

        // Do another query on the handle for the non-existent names.  Leave SL_RESTART_SCAN set.
        NTSTATUS repeatStatus = NtQueryDirectoryFile(enumHandle.get(),
            nullptr,
            nullptr,
            nullptr,
            &ioStatus,
            buffer,
            ARRAYSIZE(buffer),
            FileFullDirectoryInformation,
            FALSE,
            &fileSpec,
            restartScan);

        // Check expected status code for a repeat query on a given handle for a non-existent name.
        VERIFY_ARE_EQUAL(STATUS_NO_MORE_FILES, repeatStatus);

        // Once more, this time without SL_RESTART_SCAN.
        restartScan = false;
        NTSTATUS finalStatus = NtQueryDirectoryFile(enumHandle.get(),
            nullptr,
            nullptr,
            nullptr,
            &ioStatus,
            buffer,
            ARRAYSIZE(buffer),
            FileFullDirectoryInformation,
            false,
            &fileSpec,
            restartScan);

        // Check expected status code for a repeat query on a given handle for a non-existent name.
        VERIFY_ARE_EQUAL(STATUS_NO_MORE_FILES, finalStatus);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_EnumTestQueryDirectoryFileRestartScanProjectedFile(const char* virtualRootPath)
{
    std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumTestQueryDirectoryFileRestartScanResetsFilter");

    try
    {
        std::shared_ptr<void> enumHandle = OpenForRead(folderPath);

        std::vector<std::wstring> expectedResults = { L".", L"..", L"fileInPackage1.txt", L"fileInPackage2.txt", L"fileInPackage3.txt" };
        VerifyEnumerationMatches(enumHandle.get(), expectedResults);

        // Query again, resetting the scan to the start.
        VerifyEnumerationMatches(enumHandle.get(), expectedResults);

        // Query again, using a filter
        UNICODE_STRING fileSpec;
        RtlInitUnicodeString(&fileSpec, L"fileInPackage2.txt");
        expectedResults = { L"fileInPackage2.txt", };
        VerifyEnumerationMatches(enumHandle.get(), &fileSpec, expectedResults);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}