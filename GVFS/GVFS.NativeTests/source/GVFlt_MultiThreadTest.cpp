#include "stdafx.h"
#include "GVFlt_MultiThreadsTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFLT_MultiThreadTest");

static void ReadFileThreadProc(HANDLE& hFile, const std::string& path)
{
    hFile = CreateFile(path.c_str(),
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        VERIFY_FAIL("CreateFile failed");
    }
}

bool GVFlt_OpenForReadsSameTime(const char* virtualRootPath)
{
    try
    {
        std::string scratchTestRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OpenForReadsSameTime\\";

        const int threadCount = 10;
        std::thread threadList[threadCount];
        std::array<HANDLE, threadCount> handles;
        for (auto i = 0; i < threadCount; i++) {
            threadList[i] = std::thread(ReadFileThreadProc, std::ref(handles[i]), scratchTestRoot + "test");
        }

        for (auto i = 0; i < threadCount; i++) {
            threadList[i].join();
        }

        for (HANDLE hFile : handles)
        {
            CloseHandle(hFile);
        }
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

static void WriteFileThreadProc(HANDLE& hFile, const std::string& path)
{
    hFile = CreateFile(path.c_str(),
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        VERIFY_FAIL("CreateFile failed");
    }
}

bool GVFlt_OpenForWritesSameTime(const char* virtualRootPath)
{
    try
    {
        std::string scratchTestRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OpenForWritesSameTime\\";

        const int threadCount = 10;
        std::thread threadList[threadCount];
        std::array<HANDLE, threadCount> handles;
        for (auto i = 0; i < threadCount; i++) {
            threadList[i] = std::thread(WriteFileThreadProc, std::ref(handles[i]), scratchTestRoot + "test");
        }

        for (auto i = 0; i < threadCount; i++) {
            threadList[i].join();
        }

        for (HANDLE hFile : handles)
        {
            CloseHandle(hFile);
        }
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}
