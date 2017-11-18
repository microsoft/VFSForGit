#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_ModifyFileInScratchAndDir(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_RMDIRTest1(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_RMDIRTest2(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_RMDIRTest3(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_RMDIRTest4(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_RMDIRTest5(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeepNonExistFileUnderPartial(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_SupersededReparsePoint(const char* virtualRootPath);

	// Note the following tests were not ported from GVFlt:
	//
	// StartInstanceAndFreeCallbacks
	// QickAttachDetach
	//   - These timing scenarios don't need to be tested with RGFS
	//
	// UnableToReadPartialFile
	//   - This test requires control over the GVFlt callback implementation
	//
	// DeepNonExistFileUnderFull
	//   - Currently RGFS does not covert folders to full

	// The following were ported to the managed tests:
	//
	// CMDHangNoneActiveInstance
}
