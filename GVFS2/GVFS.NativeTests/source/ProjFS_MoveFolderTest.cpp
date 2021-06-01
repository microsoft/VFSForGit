#include "stdafx.h"
#include "ProjFS_MoveFolderTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_MoveFolderTest");

bool ProjFS_MoveFolder_NoneToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "NoneToNone\\";

        int error = MovFile(testScratchRoot + "fromfolderNotExist", testScratchRoot + "tofolderNotExist");
        VERIFY_ARE_EQUAL((int)ENOENT, error);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "to"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_VirtualToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToNone\\";

        int error = MovFile(testScratchRoot + "from", testScratchRoot + "tofolderNotExist");
        VERIFY_ARE_EQUAL((int)EINVAL, error);

        // VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\notexistInTo.txt"));
        // VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "tofolderNotExist\\notexistInTo.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_PartialToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "PartialToNone\\";

        ReadFileAsString(testScratchRoot + "from\\notexistInTo.txt");
        int error = MovFile(testScratchRoot + "from", testScratchRoot + "tofolderNotExist");
        VERIFY_ARE_EQUAL((int)EINVAL, error);

        // VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\notexistInTo.txt"));
        // VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "tofolderNotExist\\notexistInTo.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_VirtualToVirtual(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToVirtual\\";

        int error = MovFile(testScratchRoot + "from", testScratchRoot + "to");
        VERIFY_ARE_EQUAL((int)EINVAL, error);
        /*
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to"));
        */
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_VirtualToPartial(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToPartial\\";

        ReadFileAsString(testScratchRoot + "to\\notexistInFrom.txt");
        int error = MovFile(testScratchRoot + "from", testScratchRoot + "to");
        VERIFY_ARE_EQUAL((int)EINVAL, error);

        /*
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to"));
        */
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_OutsideToNone(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToNone\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToNone";
        CreateDirectoryWithIntermediates(testLayerRoot);

        int error = MovFile(testLayerRoot, testScratchRoot + "notexists");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testLayerRoot));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "notexists"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_OutsideToVirtual(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToVirtual\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToVirtual\\";
        CreateDirectoryWithIntermediates(testLayerRoot);

        int error = MovFile(testLayerRoot, testScratchRoot);
        VERIFY_ARE_EQUAL((int)EEXIST, error);
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to"));

        error = MovFile(testLayerRoot, testScratchRoot + "to");
        VERIFY_ARE_EQUAL((int)EEXIST, error);
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_NoneToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "NoneToOutside\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "NoneToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot);

        int error = MovFile(testScratchRoot + "NotExist", testLayerRoot);
        VERIFY_ARE_EQUAL((int)ENOENT, error);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_VirtualToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToOutside\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "VirtualToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot);

        int error = MovFile(testScratchRoot + "from", testLayerRoot);
        VERIFY_ARE_EQUAL((int)EINVAL, error);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFolder_OutsideToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToOutside\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot + "from\\");
        CreateNewFile(testLayerRoot + "from\\" + "test.txt", "test data");

        int error = MovFile(testLayerRoot + "from\\", testLayerRoot + "to\\");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testLayerRoot + "from\\" + "test.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "to\\" + "test.txt"));

        VERIFY_ARE_EQUAL("test data", ReadFileAsString(testLayerRoot + "to\\" + "test.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}
