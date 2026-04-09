#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_ToVirtualFile(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_ToPlaceHolder(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_ToFullFile(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_ToNonExistFileWillFail(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_NameAlreadyExistWillFail(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_FromOutside(const char* pathOutsideRepo, const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SetLink_ToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
}
