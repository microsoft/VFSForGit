#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_OpenForReadsSameTime(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_OpenForWritesSameTime(const char* virtualRootPath);

    // Note the following tests were not ported from ProjFS:
    //
    // GetPlaceholderInfoAndStopInstance
    // GetStreamAndStopInstance
    // EnumAndStopInstance
    //    - These tests require precise control of when the virtualization instance is stopped

    // Note: ProjFS_OpenMultipleFilesForReadsSameTime was not ported from ProjFS code, it just follows
    // the same pattern as those tests
    NATIVE_TESTS_EXPORT bool ProjFS_OpenMultipleFilesForReadsSameTime(const char* virtualRootPath);
}
