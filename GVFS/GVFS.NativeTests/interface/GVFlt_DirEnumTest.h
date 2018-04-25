#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_EnumEmptyFolder(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_EnumFolderWithOneFileInPackage(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_EnumFolderWithOneFileInBoth(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_EnumFolderWithOneFileInBoth1(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_EnumFolderDeleteExistingFile(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_EnumFolderSmallBuffer(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool GVFlt_EnumTestNoMoreNoSuchReturnCodes(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool GVFlt_EnumTestQueryDirectoryFileRestartScanProjectedFile(const char* virtualRootPath);
}
