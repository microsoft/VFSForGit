#include "stdafx.h"
#include "ProjFS_DeleteFileTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_DeleteFileTest");

bool ProjFS_DeleteVirtualFile_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteVirtualFile_SetDisposition\\");
        DWORD error = DelFile(testScratchRoot + "a.txt");
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteVirtualFile_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteVirtualFile_DeleteOnClose\\");
        DWORD error = DelFile(testScratchRoot + "a.txt", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeletePlaceholder_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeletePlaceholder_SetDisposition\\");

        // make a.txt a placeholder
        ReadFileAsString(testScratchRoot + "a.txt");
        DWORD error = DelFile(testScratchRoot + "a.txt");
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeletePlaceholder_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeletePlaceholder_DeleteOnClose\\");

        // make a.txt a placeholder
        ReadFileAsString(testScratchRoot + "a.txt");
        DWORD error = DelFile(testScratchRoot + "a.txt", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteFullFile_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteFullFile_SetDisposition\\");

        // make a.txt a full file
        WriteToFile(testScratchRoot + "a.txt", "123123");
        DWORD error = DelFile(testScratchRoot + "a.txt");
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteFullFile_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteFullFile_DeleteOnClose\\");

        // make a.txt a full file
        WriteToFile(testScratchRoot + "a.txt", "123123");
        DWORD error = DelFile(testScratchRoot + "a.txt", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "a.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteLocalFile_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteLocalFile_SetDisposition\\");

        CreateNewFile(testScratchRoot + "c3.txt", "123123");
        DWORD error = DelFile(testScratchRoot + "c3.txt");
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "a.txt", "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "c3.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteLocalFile_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteLocalFile_DeleteOnClose\\");

        CreateNewFile(testScratchRoot + "c4.txt", "123123");
        DWORD error = DelFile(testScratchRoot + "c4.txt", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "a.txt", "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "c4.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNotExistFile_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNotExistFile_SetDisposition\\");

        DWORD error = DelFile(testScratchRoot + "notexist.txt");
        VERIFY_ARE_EQUAL((DWORD)ERROR_FILE_NOT_FOUND, error);

        std::vector<std::string> expected = { "a.txt", "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNotExistFile_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNotExistFile_DeleteOnClose\\");

        DWORD error = DelFile(testScratchRoot + "notexist.txt", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_FILE_NOT_FOUND, error);

        std::vector<std::string> expected = { "a.txt", "b.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNonRootVirtualFile_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNonRootVirtualFile_SetDisposition\\");
        std::string testFolder = "A\\B\\C\\D\\";
        std::string testFile = "test.txt";

        DWORD error = DelFile(testScratchRoot + testFolder + testFile);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = {};
        ExpectDirEntries(testScratchRoot + testFolder, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + testFolder));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + testFolder + testFile));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNonRootVirtualFile_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNonRootVirtualFile_DeleteOnClose\\");
        std::string testFolder = "A1\\B\\C\\D\\";
        std::string testFile = "test.txt";

        DWORD error = DelFile(testScratchRoot + testFolder + testFile, false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = {};
        ExpectDirEntries(testScratchRoot + testFolder, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + testFolder));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + testFolder + testFile));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteFileOutsideVRoot_SetDisposition(const char* pathOutsideRepo)
{
    try
    {
        std::string testFile = pathOutsideRepo + std::string("\\GVFlt_DeleteFileOutsideVRoot_SetDisposition.txt");
        CreateNewFile(testFile);

        DWORD error = DelFile(testFile);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);
        VERIFY_ARE_EQUAL(false, DoesFileExist(testFile));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteFileOutsideVRoot_DeleteOnClose(const char* pathOutsideRepo)
{
    try
    {
        std::string testFile = pathOutsideRepo + std::string("\\GVFlt_DeleteFileOutsideVRoot_DeleteOnClose.txt");
        CreateNewFile(testFile);

        DWORD error = DelFile(testFile, false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);
        VERIFY_ARE_EQUAL(false, DoesFileExist(testFile));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}