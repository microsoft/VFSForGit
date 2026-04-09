#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_EnumEmptyFolder(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumFolderWithOneFileInPackage(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumFolderWithOneFileInBoth(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumFolderWithOneFileInBoth1(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumFolderDeleteExistingFile(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumFolderSmallBuffer(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumTestNoMoreNoSuchReturnCodes(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_EnumTestQueryDirectoryFileRestartScanProjectedFile(const char* virtualRootPath);
}
