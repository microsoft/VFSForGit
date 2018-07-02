#include "stdafx.h"
#include "ProjFS_DeleteFolderTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_DeleteFolderTest");


// --------------------
//
// Special note on "EmptyFolder".  In our tests, this folder actually has a single empty file because Git
// does not allow committing empty folders.
//
// --------------------


bool ProjFS_DeleteVirtualNonEmptyFolder_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteVirtualNonEmptyFolder_SetDisposition\\");

        DWORD error = DelFolder(testScratchRoot + "NonEmptyFolder");
        VERIFY_ARE_EQUAL((DWORD)ERROR_DIR_NOT_EMPTY, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteVirtualNonEmptyFolder_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteVirtualNonEmptyFolder_DeleteOnClose\\");

        DWORD error = DelFolder(testScratchRoot + "NonEmptyFolder", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeletePlaceholderNonEmptyFolder_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeletePlaceholderNonEmptyFolder_SetDisposition\\");

        // make it a placeholder folder
        EnumDirectory(testScratchRoot + "NonEmptyFolder");

        DWORD error = DelFolder(testScratchRoot + "NonEmptyFolder");
        VERIFY_ARE_EQUAL((DWORD)ERROR_DIR_NOT_EMPTY, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeletePlaceholderNonEmptyFolder_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeletePlaceholderNonEmptyFolder_DeleteOnClose\\");

        // make it a placeholder folder
        EnumDirectory(testScratchRoot + "NonEmptyFolder");

        DWORD error = DelFolder(testScratchRoot + "NonEmptyFolder", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteLocalEmptyFolder_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteLocalEmptyFolder_SetDisposition\\");

        // create a new local folder
        CreateDirectoryWithIntermediates(testScratchRoot + "localFolder\\");

        DWORD error = DelFolder(testScratchRoot + "localFolder");
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder\\" + "bar.txt"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteLocalEmptyFolder_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteLocalEmptyFolder_DeleteOnClose\\");

        // create a new local folder
        CreateDirectoryWithIntermediates(testScratchRoot + "localFolder\\");

        DWORD error = DelFolder(testScratchRoot + "localFolder", false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = { "EmptyFolder", "NonEmptyFolder", "TestFile.txt" };
        ExpectDirEntries(testScratchRoot, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot));

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "EmptyFolder"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "NonEmptyFolder\\" + "bar.txt"));

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "NonEmptyFolder"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNonRootVirtualFolder_SetDisposition(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNonRootVirtualFolder_SetDisposition\\");

        std::string testFolder = "A\\B\\C\\D\\";
        std::string targetFolder = "E\\";
        std::string testFile = "test.txt";

        // NOTE: Deviate from ProjFS's DeleteNonRootVirtualFolder_SetDisposition here by deleting a file first
        // Git will not allow empty folders to be commited, and so \E must have a file in it
        DWORD fileError = DelFile(testScratchRoot + testFolder + targetFolder + testFile);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, fileError);

        DWORD error = DelFolder(testScratchRoot + testFolder + targetFolder);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = {};
        ExpectDirEntries(testScratchRoot + testFolder, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + testFolder));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + testFolder + targetFolder));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_DeleteNonRootVirtualFolder_DeleteOnClose(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_DeleteNonRootVirtualFolder_DeleteOnClose\\");

        std::string testFolder = "A\\B\\C\\D\\";
        std::string targetFolder = "E\\";
        std::string testFile = "test.txt";

        // NOTE: Deviate from ProjFS's DeleteNonRootVirtualFolder_DeleteOnClose here by deleting a file first
        // Git will not allow empty folders to be commited, and so \E must have a file in it
        DWORD fileError = DelFile(testScratchRoot + testFolder + targetFolder + testFile);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, fileError);

        DWORD error = DelFolder(testScratchRoot + testFolder + targetFolder, false);
        VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, error);

        std::vector<std::string> expected = {};
        ExpectDirEntries(testScratchRoot + testFolder, expected);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + testFolder));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + testFolder + targetFolder));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}