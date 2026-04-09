#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_OpenRootFolder(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_WriteAndVerify(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeleteExistingFile(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_OpenNonExistingFile(const char* virtualRootPath);

    // Note the following tests were not ported from ProjFS:
    //
    // OpenFileForRead
    //    - Covered in GVFS.FunctionalTests.Tests.EnlistmentPerFixture.WorkingDirectoryTests.ProjectedFileHasExpectedContents
    // OpenFileForWrite
    //    - Covered in GVFS.FunctionalTests.Tests.LongRunningEnlistment.WorkingDirectoryTests.ShrinkFileContents (and other tests)
    // ReadFileAndVerifyContent
    //    - Covered in GVFS.FunctionalTests.Tests.EnlistmentPerFixture.WorkingDirectoryTests.ProjectedFileHasExpectedContents
    // WriteFileAndVerifyFileInScratch
    // OverwriteAndVerify
    //    - Does not apply: Tests that writing scratch layer does not impact backing layer contents
    // CreateNewFileInScratch
    // CreateNewFileAndWriteInScratch
    //    - Covered in GVFS.FunctionalTests.Tests.LongRunningEnlistment.WorkingDirectoryTests.ShrinkFileContents (and other tests)
}
