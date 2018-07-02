#include "stdafx.h"
#include "ProjFS_MultiThreadsTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFLT_MultiThreadTest");

static bool ReadFileThreadProc(HANDLE& hFile, const std::string& path, LONGLONG expectedSize, std::shared_future<void> mainThreadReadyFuture, std::promise<void>& threadReadyPromise)
{
    // Notify the main thread that our thread is ready
    threadReadyPromise.set_value();

    // Wait for the main thread to ask us to start
    mainThreadReadyFuture.wait();

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
        return false;
    }

    LARGE_INTEGER fileSize;
    VERIFY_ARE_NOT_EQUAL(GetFileSizeEx(hFile, &fileSize), 0);
    VERIFY_ARE_EQUAL(fileSize.QuadPart, expectedSize);

    return true;
}

bool ProjFS_OpenForReadsSameTime(const char* virtualRootPath)
{
    try
    {
        std::string scratchTestRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OpenForReadsSameTime\\";

        const int threadCount = 10;
        std::array<HANDLE, threadCount> handles;
        
        std::promise<void> mainThreadReadyPromise;
        std::shared_future<void> mainThreadReadyFuture(mainThreadReadyPromise.get_future());
        std::vector<std::promise<void>> threadReadyPromises;
        threadReadyPromises.resize(threadCount);
        std::vector<std::future<bool>> threadCompleteFutures;

        // Start std::async for each thread
        for (size_t i = 0; i < threadReadyPromises.size(); ++i)
        {
            std::promise<void>& promise = threadReadyPromises[i];
            threadCompleteFutures.push_back(std::async(
                std::launch::async, 
                ReadFileThreadProc, 
                std::ref(handles[i]), 
                scratchTestRoot + "test", 
                6,
                mainThreadReadyFuture, 
                std::ref(promise)));
        }

        // Wait for all threads to become ready
        for (std::promise<void>& promise : threadReadyPromises)
        {
            promise.get_future().wait();
        }

        // Signal the threads to run
        mainThreadReadyPromise.set_value();

        // Wait for threads to complete
        for (std::future<bool>& openFuture : threadCompleteFutures)
        {
            openFuture.get();
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

bool ProjFS_OpenMultipleFilesForReadsSameTime(const char* virtualRootPath)
{
    try
    {
        std::string scratchTestRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OpenMultipleFilesForReadsSameTime\\";
        std::string scratchTestRoot2 = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OpenMultipleFilesForReadsSameTime_2\\";

        const char threadCount = 8;
        std::array<HANDLE, threadCount> handles;
        std::promise<void> mainThreadReadyPromise;
        std::shared_future<void> mainThreadReadyFuture(mainThreadReadyPromise.get_future());
        std::vector<std::promise<void>> threadReadyPromises;
        threadReadyPromises.resize(threadCount);
        std::vector<std::future<bool>> threadCompleteFutures;

        // Start std::async for each thread
        for (size_t i = 0; i < threadReadyPromises.size(); ++i)
        {
            // Files are named:
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime\test1
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime\test2
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime\test3
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime\test4
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime_2\test5
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime_2\test6
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime_2\test7
            // GVFlt_MultiThreadTest\OpenMultipleFilesForReadsSameTime_2\test8

            std::promise<void>& promise = threadReadyPromises[i];
            threadCompleteFutures.push_back(std::async(
                std::launch::async,
                ReadFileThreadProc,
                std::ref(handles[i]),
                (i <= 3 ? scratchTestRoot : scratchTestRoot2) + "test" + static_cast<char>('0' + i + 1),
                13704 + i, // expected size
                mainThreadReadyFuture,
                std::ref(promise)));
        }

        // Wait for all threads to become ready
        for (std::promise<void>& promise : threadReadyPromises)
        {
            promise.get_future().wait();
        }

        // Signal the threads to run
        mainThreadReadyPromise.set_value();

        // Wait for threads to complete
        for (std::future<bool>& openFuture : threadCompleteFutures)
        {
            openFuture.get();
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

bool ProjFS_OpenForWritesSameTime(const char* virtualRootPath)
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
