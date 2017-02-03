#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteVirtualFile_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteVirtualFile_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeletePlaceholder_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeletePlaceholder_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteFullFile_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteFullFile_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteLocalFile_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteLocalFile_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteNotExistFile_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteNotExistFile_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteNonRootVirtualFile_SetDisposition(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteNonRootVirtualFile_DeleteOnClose(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteFileOutsideVRoot_SetDisposition(const char* pathOutsideRepo);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteFileOutsideVRoot_DeleteOnClose(const char* pathOutsideRepo);

	// Note the following tests were not ported from GVFlt:
	//
	// DeleteFullFileWithoutFileContext_SetDisposition
	// DeleteFullFileWithoutFileContext_DeleteOnClose
	//    - GVFS will always project new files when its back layer changes 
}
