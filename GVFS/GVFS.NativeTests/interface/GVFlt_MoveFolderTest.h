#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_NoneToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_VirtualToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_PartialToNone(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_VirtualToVirtual(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_VirtualToPartial(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_OutsideToNone(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_OutsideToVirtual(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_NoneToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_VirtualToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_MoveFolder_OutsideToOutside(const char* pathOutsideRepo, const char* virtualRootPath);
}
