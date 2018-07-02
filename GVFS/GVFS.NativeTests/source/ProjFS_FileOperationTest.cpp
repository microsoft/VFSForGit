#include "stdafx.h"
#include "ProjFS_FileOperationTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_FileOperationTest");

bool ProjFS_OpenRootFolder(const char* virtualRootPath)
{
    try
    {
        OpenForRead(virtualRootPath);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_WriteAndVerify(const char* virtualRootPath)
{
    try
    {
        std::string scratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\";
        std::string fileName = "WriteAndVerify.txt";
        std::string data = "test data\r\n";

        // write file in scratch
        std::string newData = "new data";
        WriteToFile(scratchRoot + fileName, newData, false);

        std::string newContent = newData + data.substr(newData.size());

        VERIFY_ARE_EQUAL(newContent, ReadFileAsString(scratchRoot + fileName));
        VERIFY_ARE_EQUAL(newContent, ReadFileAsStringUncached(scratchRoot + fileName));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteExistingFile(const char* virtualRootPath)
{
    try
    {
        std::string scratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\";
        std::string fileName = "DeleteExistingFile.txt";

        std::string fileInScratch = scratchRoot + fileName;
        // delete in scratch root
        VERIFY_ARE_EQUAL(TRUE, DeleteFile(fileInScratch.c_str()));

        // make sure the file can't be opened again
        auto hFileInScratch = CreateFile(fileInScratch.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            NULL);

        VERIFY_ARE_EQUAL(INVALID_HANDLE_VALUE, hFileInScratch);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_OpenNonExistingFile(const char* virtualRootPath)
{
    try
    {
        std::string scratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\";
        std::string fileName = "OpenNonExistingFile.txt";

        std::string fileInScratch = scratchRoot + fileName;

        // make sure the file can't be opened and last error is file not found
        HANDLE hFileInScratch = CreateFile(fileInScratch.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            NULL);

        VERIFY_ARE_EQUAL(INVALID_HANDLE_VALUE, hFileInScratch);
        VERIFY_ARE_EQUAL(ERROR_FILE_NOT_FOUND, (HRESULT)GetLastError());
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}