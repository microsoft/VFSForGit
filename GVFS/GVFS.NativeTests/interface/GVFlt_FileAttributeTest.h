#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_ModifyFileInScratchAndCheckLastWriteTime(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_FileSize(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_ModifyFileInScratchAndCheckFileSize(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_FileAttributes(const char* virtualRootPath);

	// Note the following tests were not ported from GVFlt:
	//
	// LastWriteTime
	//     - There is no last write time in the GVFS layer to compare with
}
