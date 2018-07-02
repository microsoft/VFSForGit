#pragma once

extern "C"
{
    NATIVE_TESTS_EXPORT bool ProjFS_ModifyFileInScratchAndDir(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_RMDIRTest1(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_RMDIRTest2(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_RMDIRTest3(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_RMDIRTest4(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_RMDIRTest5(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_DeepNonExistFileUnderPartial(const char* virtualRootPath);
    NATIVE_TESTS_EXPORT bool ProjFS_SupersededReparsePoint(const char* virtualRootPath);

    // Note the following tests were not ported from ProjFS:
    //
    // StartInstanceAndFreeCallbacks
    // QickAttachDetach
    //   - These timing scenarios don't need to be tested with GVFS
    //
    // UnableToReadPartialFile
    //   - This test requires control over the ProjFS callback implementation
    //
    // DeepNonExistFileUnderFull
    //   - Currently GVFS does not covert folders to full

    // The following were ported to the managed tests:
    //
    // CMDHangNoneActiveInstance
}
