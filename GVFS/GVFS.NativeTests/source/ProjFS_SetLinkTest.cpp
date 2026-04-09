#include "stdafx.h"
#include "ProjFS_SetLinkTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_SetLinkTest");
static const std::string _testFile("test.txt");
static const std::string _data("test data");

bool ProjFS_SetLink_ToVirtualFile(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "ToVirtualFile\\";

        bool created = NewHardLink(testScratchRoot + "newlink", testScratchRoot + _testFile);
        VERIFY_ARE_EQUAL(true, created);

        AreEqual(_data, ReadFileAsString(testScratchRoot + "newlink"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_ToPlaceHolder(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "ToPlaceHolder\\";

        ReadFileAsString(testScratchRoot + _testFile);

        bool created = NewHardLink(testScratchRoot + "newlink", testScratchRoot + _testFile);
        VERIFY_ARE_EQUAL(true, created);

        AreEqual(_data, ReadFileAsString(testScratchRoot + "newlink"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_ToFullFile(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "ToFullFile\\";

        WriteToFile(testScratchRoot + _testFile, "new content");

        bool created = NewHardLink(testScratchRoot + "newlink", testScratchRoot + _testFile);
        VERIFY_ARE_EQUAL(true, created);

        AreEqual("new content", ReadFileAsString(testScratchRoot + _testFile));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_ToNonExistFileWillFail(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "ToNonExistFileWillFail\\";

        bool created = NewHardLink(testScratchRoot + "newlink", testScratchRoot + "nonexist");
        VERIFY_ARE_EQUAL(false, created);
        VERIFY_ARE_EQUAL((DWORD)ERROR_FILE_NOT_FOUND, GetLastError());
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_NameAlreadyExistWillFail(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "NameAlreadyExistWillFail\\";

        bool created = NewHardLink(testScratchRoot + "foo.txt", testScratchRoot + _testFile);
        VERIFY_ARE_EQUAL(false, created);
        VERIFY_ARE_EQUAL((DWORD)ERROR_ALREADY_EXISTS, GetLastError());
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_FromOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "FromOutside\\";

        bool created = NewHardLink(std::string(pathOutsideRepo) + "\\" + "FromOutsideLink", testScratchRoot + _testFile);
        VERIFY_ARE_EQUAL(true, created);
        AreEqual(_data, ReadFileAsString(std::string(pathOutsideRepo) + "\\" + "FromOutsideLink"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_SetLink_ToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "ToOutside\\";

        CreateNewFile(std::string(pathOutsideRepo) + "\\" + _testFile, _data);
        bool created = NewHardLink(testScratchRoot + "newlink", std::string(pathOutsideRepo) + "\\" + _testFile);
        VERIFY_ARE_EQUAL(true, created);
        AreEqual(_data, ReadFileAsString(testScratchRoot + "newlink"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}