#include "stdafx.h"
#include "ProjFS_FileEATest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "Should.h"

using namespace TestHelpers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_FileEATest");

bool ProjFS_OneEAAttributeWillPass(const char* virtualRootPath)
{
    try
    {
        std::string fileName = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\OneEAAttributeWillPass.txt");

        ULONG size = 2 * 65535;
        PFILE_FULL_EA_INFORMATION buffer = (PFILE_FULL_EA_INFORMATION)calloc(1, size);
        auto status = SetEAInfo(fileName, buffer, size);
        VERIFY_ARE_EQUAL(STATUS_SUCCESS, status);

        OpenForRead(fileName);
        status = ReadEAInfo(fileName, buffer, &size);
        VERIFY_ARE_EQUAL(STATUS_SUCCESS, status);
        VERIFY_ARE_NOT_EQUAL((ULONG)0, size);

    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}