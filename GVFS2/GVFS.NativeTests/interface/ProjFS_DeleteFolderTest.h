#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteVirtualNonEmptyFolder_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteVirtualNonEmptyFolder_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeletePlaceholderNonEmptyFolder_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeletePlaceholderNonEmptyFolder_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteLocalEmptyFolder_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteLocalEmptyFolder_DeleteOnClose(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNonRootVirtualFolder_SetDisposition(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteNonRootVirtualFolder_DeleteOnClose(const char* virtualRootPath);

    // Note the following tests were not ported from ProjFS:
    //
    // DeleteVirtualEmptyFolder_SetDisposition
    // DeleteVirtualEmptyFolder_DeleteOnClose
    //    - Git does not support empty folders
    //
    // DeleteFullNonEmptyFolder_SetDisposition
    // DeleteFullNonEmptyFolder_DeleteOnClose
    //    - GVFS does not allow full folders
}
