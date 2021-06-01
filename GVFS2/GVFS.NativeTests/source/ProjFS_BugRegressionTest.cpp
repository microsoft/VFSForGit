#include "stdafx.h"
#include "ProjFS_BugRegressionTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_BugRegressionTest");

bool ProjFS_ModifyFileInScratchAndDir(const char* virtualRootPath)
{
    // For bug #7700746 - File size is not updated when writing to a file projected from ProjFS app (e.g. GVFS, test app)

    try
    {
        std::string testScratch = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_ModifyFileInScratchAndDir\\");
        std::string fileName = "ModifyFileInScratchAndDir.txt";

        WriteToFile(testScratch + fileName, "ModifyFileInScratchAndDir:test data", false);

        std::vector<FileInfo> entries = EnumDirectory(testScratch);
        VERIFY_ARE_EQUAL((size_t)3, entries.size());
        VERIFY_ARE_EQUAL(fileName, entries[2].Name);
        VERIFY_ARE_EQUAL(true, entries[2].IsFile);
        VERIFY_ARE_EQUAL(35, (LONGLONG)entries[2].FileSize);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

void RMDIR(const std::string& path)
{
    WIN32_FIND_DATA ffd;

    std::vector<std::string> folders;

    std::string query = path + "*";

    HANDLE hFind = FindFirstFile(query.c_str(), &ffd);

    if (hFind == INVALID_HANDLE_VALUE)
    {
        VERIFY_FAIL("FindFirstFile failed");
    }

    do
    {
        if (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            folders.push_back(ffd.cFileName);
        }
        else {
            auto fileName = CombinePath(path, ffd.cFileName);

            if (FALSE == DeleteFile(fileName.c_str())) {
                VERIFY_FAIL("DeleteFile failed");
            }
        }
    } while (FindNextFile(hFind, &ffd) != 0);

    auto dwError = GetLastError();
    if (dwError != ERROR_NO_MORE_FILES)
    {
        VERIFY_FAIL("FindNextFile failed");
    }

    FindClose(hFind);

    for (auto folder : folders) {
        if (folder != "." && folder != "..") {
            RMDIR(folder);
        }
    }

    if (FALSE == RemoveDirectory(path.c_str())) {
        VERIFY_FAIL("RemoveDirectory failed");
    }
}

void RMDIRTEST(const std::string& virtualRootPath, const std::string& testName, const std::vector<std::string>& fileNamesInScratch)
{
    std::string testCaseScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + testName + "\\";

    for (const std::string& fileName : fileNamesInScratch) {
        CreateNewFile(testCaseScratchRoot + fileName);
    }

    RMDIR(testCaseScratchRoot);

    auto handle = CreateFile((TEST_ROOT_FOLDER + "\\" + testName).c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    VERIFY_ARE_EQUAL(INVALID_HANDLE_VALUE, handle);
}

bool ProjFS_RMDIRTest1(const char* virtualRootPath)
{
    // Bug #7703883 - ProjFS: RMDIR /s against a partial folder returns directory not empty error

    try
    {
        // layer: 1, 2
        // scratch: 3
        std::vector<std::string> scratchNames = { "3" };
        RMDIRTEST(virtualRootPath, "RMDIRTest1", scratchNames);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_RMDIRTest2(const char* virtualRootPath)
{
    try
    {
        // layer: 1
        // scratch: 2, 3
        std::vector<std::string> scratchNames = { "2", "3" };
        RMDIRTEST(virtualRootPath, "RMDIRTest2", scratchNames);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_RMDIRTest3(const char* virtualRootPath)
{
    try
    {
        // layer: 1, 3
        // scratch: 2
        std::vector<std::string> scratchNames = { "2" };
        RMDIRTEST(virtualRootPath, "RMDIRTest3", scratchNames);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_RMDIRTest4(const char* virtualRootPath)
{
    try
    {
        // layer: 2
        // scratch: 1, 3
        std::vector<std::string> scratchNames = { "1", "3" };
        RMDIRTEST(virtualRootPath, "RMDIRTest4", scratchNames);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_RMDIRTest5(const char* virtualRootPath)
{
    try
    {
        // layer: 1, 2, 4
        // scratch: 2, 3
        std::string testCaseScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\RMDIRTest5\\";
        OpenForRead(testCaseScratchRoot + "2");

        std::vector<std::string> scratchNames = { "3" };
        RMDIRTEST(virtualRootPath, "RMDIRTest5", scratchNames);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeepNonExistFileUnderPartial(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\DeepNonExistFileUnderPartial\\";

        // try to open a deep non existing file
        CreateFile((testScratchRoot + "a\\b\\c\\d\\e").c_str(),
            GENERIC_READ,
            FILE_SHARE_READ,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

        VERIFY_ARE_EQUAL((DWORD)ERROR_PATH_NOT_FOUND, GetLastError());
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SupersededReparsePoint(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\SupersededReparsePoint\\";

        std::string path = testScratchRoot + "test.txt";

        std::shared_ptr<void> openThreadHandle;
        std::shared_ptr<void> openThread1Handle;
        std::shared_ptr<void> truncateThreadHandle;

        std::thread openThread([path, &openThreadHandle]() {

            openThreadHandle = std::shared_ptr<void>(
                CreateFile(path.c_str(),
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    NULL),
                CloseHandle);

            if (openThreadHandle.get() == INVALID_HANDLE_VALUE)
            {
                VERIFY_FAIL("CreateFile for read failed (openThread)");
            }
        });

        std::thread openThread1([path, &openThread1Handle]() {

            openThread1Handle = std::shared_ptr<void>(
                CreateFile(path.c_str(),
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    NULL),
                CloseHandle);

            if (openThread1Handle.get() == INVALID_HANDLE_VALUE)
            {
                VERIFY_FAIL("CreateFile for read failed (openThread1)");
            }
        });

        std::thread truncateThread([path, &truncateThreadHandle]() {
            truncateThreadHandle = std::shared_ptr<void>(
                CreateFile(path.c_str(),
                    GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    TRUNCATE_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    NULL),
                CloseHandle);

            if (truncateThreadHandle.get() == INVALID_HANDLE_VALUE)
            {
                VERIFY_FAIL("CreateFile for truncate failed (openThread1)");
            }
        });

        openThread.join();
        openThread1.join();
        truncateThread.join();
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}