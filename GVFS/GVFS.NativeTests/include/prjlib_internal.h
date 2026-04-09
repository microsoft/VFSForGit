// prjlib_internal.h
//
// Function declarations for internal functions in prjlib (used in the ProjFS tests)
// that are not intended to be used by user applications (e.g. GVFS) built on GVFlt

#pragma once

#include "prjlibp.h"

#ifdef __cplusplus
extern "C" {
#endif

//
// Functions operating on GVFS reparse points
//
HRESULT
PrjpReadPrjReparsePointData(
    _In_ LPCWSTR FilePath,
    _Out_writes_bytes_(*DataSize) PGV_REPARSE_INFO ReparsePointData,
    _Out_opt_ PULONG ReparseTag,
    _Inout_ PUSHORT DataSize
);

#ifdef __cplusplus
}
#endif