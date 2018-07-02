#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteVirtualFile_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteVirtualFile_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeletePlaceholder_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeletePlaceholder_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteFullFile_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteFullFile_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteLocalFile_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteLocalFile_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNotExistFile_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNotExistFile_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNonRootVirtualFile_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNonRootVirtualFile_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteFileOutsideVRoot_SetDisposition(const char* pathOutsideRepo);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteFileOutsideVRoot_DeleteOnClose(const char* pathOutsideRepo);

    // Note the following tests were not ported from ProjFS:
    //
    // DeleteFullFileWithoutFileContext_SetDisposition
    // DeleteFullFileWithoutFileContext_DeleteOnClose
    //    - GVFS will always project new files when its back layer changes 
}
