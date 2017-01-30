#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_NoneToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_PartialToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_FullToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_LocalToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToVirtual(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToVirtualFileNameChanged(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToPartial(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_PartialToPartial(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_LocalToVirtual(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToVirtualIntermidiateDirNotExist(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToNoneIntermidiateDirNotExist(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_OutsideToNone(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_OutsideToVirtual(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_OutsideToPartial(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_NoneToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_VirtualToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_PartialToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_OutsideToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFile_LongFileName(const char* virtualRootPath);

	// Note the following tests were not ported from GVFlt:
	//
	// VirtualToFull
	//    - GVFS does not allow full folders
}
