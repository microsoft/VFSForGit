// gvlib_internal.h
//
// Function declarations for internal functions in gvlib (used in the GVFlt tests)
// that are not intended to be used by user applications (e.g. RGFS) built on GVFlt
//
// Subset of the contents of: sdktools\CoreBuild\GvFlt\lib\gvlib_internal.h

#pragma once

#include "gvflt.h"

#ifdef __cplusplus
extern "C" {
#endif

//
// Functions operating on RGFS reparse points
//
HRESULT
GvReadGvReparsePointData(
	_In_ LPCWSTR FilePath,
	_Out_writes_bytes_(*DataSize) PGV_REPARSE_INFO ReparsePointData,
	_Inout_ PUSHORT DataSize
);

#ifdef __cplusplus
}
#endif