#include "stdafx.h"
#include "ProjFS_MoveFileTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;
using namespace TestVerifiers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_MoveFileTest");
static const std::string _lessData = "lessData";
static const std::string _moreData = "moreData, moreData, moreData";

bool ProjFS_MoveFile_NoneToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "NoneToNone\\";

        int error = MovFile(testScratchRoot + "from\\filenotexist", testScratchRoot + "to\\filenotexist");
        VERIFY_ARE_EQUAL((int)ENOENT, error);

        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "from"));
        VERIFY_ARE_EQUAL(false, IsFullFolder(testScratchRoot + "to"));

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "ffrom\\filenotexist"));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "to\\filenotexist"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToNone\\";

        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\notexistInTo.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\notexistInTo.txt"));

        VERIFY_ARE_EQUAL(_lessData, ReadFileAsString(testScratchRoot + "to\\notexistInTo.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_PartialToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "PartialToNone\\";

        std::string expected = ReadFileAsString(testScratchRoot + "from\\lessInFrom.txt");
        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\PartialToNone.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\PartialToNone.txt"));

        AreEqual(expected, ReadFileAsString(testScratchRoot + "to\\PartialToNone.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_FullToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "FullToNone\\";

        WriteToFile(testScratchRoot + "from\\lessInFrom.txt", "testtest");
        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\FullToNone.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\FullToNone.txt"));

        AreEqual("testtest", ReadFileAsString(testScratchRoot + "to\\FullToNone.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_LocalToNone(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "LocalToNone\\";

        CreateNewFile(testScratchRoot + "from\\local.txt", "test");
        int error = MovFile(testScratchRoot + "from\\local.txt", testScratchRoot + "to\\notexistInTo.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "from\\local.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\notexistInTo.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToVirtual(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToVirtual\\";

        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToVirtualFileNameChanged(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToVirtualFileNameChanged\\";

        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\moreInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\moreInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToPartial(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToPartial\\";

        ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt");
        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_PartialToPartial(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "PartialToPartial\\";

        ReadFileAsString(testScratchRoot + "from\\lessInFrom.txt");
        ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt");
        int error = MovFile(testScratchRoot + "from\\lessInFrom.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_LocalToVirtual(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "LocalToVirtual\\";

        CreateNewFile(testScratchRoot + "from\\local.txt", _lessData);

        int error = MovFile(testScratchRoot + "from\\local.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\local.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToVirtualIntermidiateDirNotExist(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToVirtualIntermidiateDirNotExist\\";

        int error = MovFile(testScratchRoot + "from\\subFolder\\from.txt", testScratchRoot + "to\\subfolder\\to.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\subFolder\\from.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\subfolder\\to.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_VirtualToNoneIntermidiateDirNotExist(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToNoneIntermidiateDirNotExist\\";

        int error = MovFile(testScratchRoot + "from\\subFolder\\from.txt", testScratchRoot + "to\\notexist\\to.txt");
        VERIFY_ARE_EQUAL((int)ENOENT, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "from\\subFolder\\from.txt"));
        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "to\\notexist\\to.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_MoveFile_OutsideToNone(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToNone\\";

        std::string outsideFolder = std::string(pathOutsideRepo) + "\\OutsideToNone\\from\\";
        CreateDirectoryWithIntermediates(outsideFolder);
        CreateNewFile(outsideFolder + "lessInFrom.txt", _lessData);

        int error = MovFile(outsideFolder + "lessInFrom.txt", testScratchRoot + "to\\less.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\less.txt"));
        VERIFY_ARE_EQUAL(false, DoesFileExist(outsideFolder + "lessInFrom.txt"));

        AreEqual(_lessData, ReadFileAsString(testScratchRoot + "to\\less.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_MoveFile_OutsideToVirtual(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToVirtual\\";
        
        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToVirtual\\";
        CreateDirectoryWithIntermediates(testLayerRoot);
        CreateNewFile(testLayerRoot + "test.txt");

        int error = MovFile(testLayerRoot + "test.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "test.txt"));

        AreEqual(_moreData, ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_MoveFile_OutsideToPartial(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToPartial\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToPartial\\";
        CreateDirectoryWithIntermediates(testLayerRoot);
        CreateNewFile(testLayerRoot + "test.txt");

        ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt");
        int error = MovFile(testLayerRoot + "test.txt", testScratchRoot + "to\\lessInFrom.txt");
        VERIFY_ARE_EQUAL((int)EEXIST, error);

        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "test.txt"));

        AreEqual(_moreData, ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_MoveFile_NoneToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "NoneToOutside\\";

        int error = MovFile(testScratchRoot + "to\\less.txt", std::string(pathOutsideRepo) + "\\" + "from\\less.txt");
        VERIFY_ARE_EQUAL((int)ENOENT, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "to\\less.txt"));
        VERIFY_ARE_EQUAL(false, DoesFileExist(std::string(pathOutsideRepo) + "\\" + "from\\less.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}


bool ProjFS_MoveFile_VirtualToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "VirtualToOutside\\";
        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "VirtualToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot);

        int error = MovFile(testScratchRoot + "to\\lessInFrom.txt", testLayerRoot + "less.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
        // VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "less.txt"));

        AreEqual(_moreData, ReadFileAsString(testLayerRoot + "less.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_PartialToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "PartialToOutside\\";
        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "PartialToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot);

        ReadFileAsString(testScratchRoot + "to\\lessInFrom.txt");
        int error = MovFile(testScratchRoot + "to\\lessInFrom.txt", testLayerRoot + "less.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + "to\\lessInFrom.txt"));
        // VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "less.txt"));

        AreEqual(_moreData, ReadFileAsString(testLayerRoot + "less.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_OutsideToOutside(const char* pathOutsideRepo, const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "OutsideToOutside\\";

        std::string testLayerRoot = std::string(pathOutsideRepo) + "\\" + "OutsideToOutside\\";
        CreateDirectoryWithIntermediates(testLayerRoot);
        CreateNewFile(testLayerRoot + "from.txt", _lessData);

        int error = MovFile(testLayerRoot + "from.txt", testLayerRoot + "to.txt");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testLayerRoot + "from.txt"));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testLayerRoot + "to.txt"));

        AreEqual(_lessData, ReadFileAsString(testLayerRoot + "to.txt"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ProjFS_MoveFile_LongFileName(const char* virtualRootPath)
{
    try
    {
        std::string testScratchRoot = virtualRootPath + TEST_ROOT_FOLDER + "\\" + "LongFileName\\";
        std::string filename = "LLLLLLongName0ToRenameFileToWithForChangeJournalABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghojnklmopqrstuvwxyz0123456789aaaaa";

        int error = MovFile(testScratchRoot + filename, testScratchRoot + filename + "1");
        VERIFY_ARE_EQUAL((int)0, error);

        VERIFY_ARE_EQUAL(false, DoesFileExist(testScratchRoot + filename));
        VERIFY_ARE_EQUAL(true, DoesFileExist(testScratchRoot + filename + "1"));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}