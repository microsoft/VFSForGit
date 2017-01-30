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
}
