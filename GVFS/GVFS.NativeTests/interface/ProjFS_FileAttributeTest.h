#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_ModifyFileInScratchAndCheckLastWriteTime(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_FileSize(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_ModifyFileInScratchAndCheckFileSize(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_FileAttributes(const char* virtualRootPath);

    // Note the following tests were not ported from ProjFS:
    //
    // LastWriteTime
    //     - There is no last write time in the GVFS layer to compare with
}
