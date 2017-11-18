#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_OpenForReadsSameTime(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_OpenForWritesSameTime(const char* virtualRootPath);

	// Note the following tests were not ported from GVFlt:
	//
	// GetPlaceholderInfoAndStopInstance
	// GetStreamAndStopInstance
	// EnumAndStopInstance
	//    - These tests require precise control of when the virtualization instance is stopped

    // Note: GVFlt_OpenMultipleFilesForReadsSameTime was not ported from GvFlt code, it just follows
    // the same pattern as those tests
    NATIVE_TESTS_EXPORT bool GVFlt_OpenMultipleFilesForReadsSameTime(const char* virtualRootPath);
}
