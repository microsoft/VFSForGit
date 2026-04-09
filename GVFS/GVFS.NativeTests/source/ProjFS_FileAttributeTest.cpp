#include "stdafx.h"
#include "ProjFS_FileAttributeTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "Should.h"

using namespace TestHelpers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_FileAttributeTest");

bool ProjFS_ModifyFileInScratchAndCheckLastWriteTime(const char* virtualRootPath)
{
    try
    {
        std::string fileName = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\ModifyFileInScratchAndCheckLastWriteTime.txt");

        FILETIME lastWriteTime_Package = GetLastWriteTime(fileName);
        WriteToFile(fileName, "test data", false);
        FILETIME lastWriteTime_scratch = GetLastWriteTime(fileName);

        // last write time is has been updated
        // NOTE: This is slightly different than the validate in ProjFS, which tests if the scratch time is different than the layer time
        VERIFY_ARE_NOT_EQUAL(0, CompareFileTime(&lastWriteTime_Package, &lastWriteTime_scratch));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_FileSize(const char* virtualRootPath)
{
    try
    {
        std::string fileName = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\FileSize.txt");
        LARGE_INTEGER file_size_scratch = GetFileSize(fileName);
        VERIFY_ARE_EQUAL(7, file_size_scratch.QuadPart);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_ModifyFileInScratchAndCheckFileSize(const char* virtualRootPath)
{
    try
    {
        std::string fileName = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\ModifyFileInScratchAndCheckFileSize.txt");

        LARGE_INTEGER fileSize_Package = GetFileSize(fileName);

        WriteToFile(fileName, "ModifyFileInScratchAndCheckFileSize:test data", false);
        LARGE_INTEGER file_size_scratch = GetFileSize(fileName);

        VERIFY_ARE_NOT_EQUAL(fileSize_Package.QuadPart, file_size_scratch.QuadPart);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

namespace
{

void TestFileAttribute(const std::string& fileName, DWORD attribute)
{
    std::string data = "TestFileAttribute: some test data";

    BOOL success = SetFileAttributes(fileName.c_str(), attribute);
    VERIFY_ARE_EQUAL(TRUE, success);

    BOOL attrScratch = GetFileAttributes(fileName.c_str());
    VERIFY_ARE_EQUAL(attribute, attribute&attrScratch);
}

}

bool ProjFS_FileAttributes(const char* virtualRootPath)
{
    try
    {
        std::string testRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\";

        TestFileAttribute(testRoot + "FileAttributes_ARCHIVE", FILE_ATTRIBUTE_ARCHIVE);
        TestFileAttribute(testRoot + "FileAttributes_HIDDEN", FILE_ATTRIBUTE_HIDDEN);
        TestFileAttribute(testRoot + "FileAttributes_NOT_CONTENT_INDEXED", FILE_ATTRIBUTE_NOT_CONTENT_INDEXED);
        //TestFileAttribute(FILE_ATTRIBUTE_OFFLINE);
        TestFileAttribute(testRoot + "FileAttributes_READONLY", FILE_ATTRIBUTE_READONLY);
        TestFileAttribute(testRoot + "FileAttributes_SYSTEM", FILE_ATTRIBUTE_SYSTEM);
        TestFileAttribute(testRoot + "FileAttributes_TEMPORARY", FILE_ATTRIBUTE_TEMPORARY);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}