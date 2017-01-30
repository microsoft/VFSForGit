#include "stdafx.h"
#include "GVFlt_DirEnumTest.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "Should.h"

using namespace TestHelpers;

static const std::string TEST_ROOT_FOLDER("\\GVFlt_EnumTest");

bool GVFlt_EnumEmptyFolder(const char* virtualRootPath)
{
    try
    {
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumEmptyFolder\\");
        CreateDirectoryWithIntermediates(folderPath);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)2, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool GVFlt_EnumFolderWithOneFileInPackage(const char* virtualRootPath)
{
    try
    {
        std::vector<FileInfo> result = EnumDirectory(virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInPackage\\"));
        VERIFY_ARE_EQUAL((size_t)3, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL("onlyFileInFolder.txt", result[2].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool GVFlt_EnumFolderWithOneFileInBoth(const char* virtualRootPath)
{
    try
    {
        std::string existingFileName = "newfileInPackage.txt";
        std::string newFileName = "newfileInScratch.txt";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInBoth\\");
        CreateNewFile(folderPath + newFileName);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)4, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(existingFileName, result[2].Name);
        VERIFY_ARE_EQUAL(true, result[3].IsFile);
        VERIFY_ARE_EQUAL(newFileName, result[3].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool GVFlt_EnumFolderWithOneFileInBoth1(const char* virtualRootPath)
{
    try
    {
        std::string existingFileName = "newfileInPackage.txt";
        std::string newFileName = "123";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderWithOneFileInBoth1\\");
        CreateNewFile(folderPath + newFileName);

        std::vector<FileInfo> result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)4, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(newFileName, result[2].Name);
        VERIFY_ARE_EQUAL(true, result[3].IsFile);
        VERIFY_ARE_EQUAL(existingFileName, result[3].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool GVFlt_EnumFolderDeleteExistingFile(const char* virtualRootPath)
{
    try
    {
        std::string fileName1 = "fileInPackage1.txt";
        std::string fileName2 = "fileInPackage2.txt";
        std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderDeleteExistingFile\\");

        VERIFY_ARE_EQUAL(TRUE, DeleteFile((folderPath + fileName1).c_str()));

        std::vector<FileInfo>  result = EnumDirectory(folderPath);
        VERIFY_ARE_EQUAL((size_t)3, result.size());
        VERIFY_ARE_EQUAL(false, result[0].IsFile);
        VERIFY_ARE_EQUAL(strcmp(".", result[0].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(false, result[1].IsFile);
        VERIFY_ARE_EQUAL(strcmp("..", result[1].Name.c_str()), 0);
        VERIFY_ARE_EQUAL(true, result[2].IsFile);
        VERIFY_ARE_EQUAL(fileName2, result[2].Name);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool GVFlt_EnumFolderSmallBuffer(const char* virtualRootPath)
{
    std::string folderPath = virtualRootPath + TEST_ROOT_FOLDER + std::string("\\GVFlt_EnumFolderSmallBuffer");

    try
    {
        UCHAR buffer[512];
        NTSTATUS status;
        BOOLEAN restart = TRUE;
        IO_STATUS_BLOCK ioStatus;
        USHORT count = 0;

        for (USHORT i = 0; i < 26; i += 2) {

            // open every other file to create placeholders
            std::string name(1, (char)('a' + i));
            OpenForRead(folderPath + std::string("\\") + name);
        }

        std::shared_ptr<void> handle = OpenForRead(folderPath);

        do 
        {
            status = NtQueryDirectoryFile(handle.get(),
                NULL,
                NULL,
                NULL,
                &ioStatus,
                buffer,
                ARRAYSIZE(buffer),
                FileBothDirectoryInformation,
                FALSE,
                NULL,
                restart);

            if (status == STATUS_SUCCESS) {

                PFILE_BOTH_DIR_INFORMATION dirInfo;
                PUCHAR entry = buffer;

                do {

                    dirInfo = (PFILE_BOTH_DIR_INFORMATION)entry;

                    std::wstring entryName(dirInfo->FileName, dirInfo->FileNameLength / sizeof(WCHAR));

                    if ((entryName.compare(L".") != 0) && (entryName.compare(L"..") != 0)) {

                        std::wstring expectedName(1, L'a' + count);

                        VERIFY_ARE_EQUAL(entryName, expectedName);

                        count++;
                    }

                    entry = entry + dirInfo->NextEntryOffset;

                } while (dirInfo->NextEntryOffset > 0);

                restart = FALSE;
            }

        } while (status == STATUS_SUCCESS);

        VERIFY_ARE_EQUAL(count, 26);
        VERIFY_ARE_EQUAL(status, STATUS_NO_MORE_FILES);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}