#pragma once

extern "C"
{
	NATIVE_TESTS_EXPORT bool GVFlt_OpenRootFolder(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_WriteAndVerify(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_DeleteExistingFile(const char* virtualRootPath);
	NATIVE_TESTS_EXPORT bool GVFlt_OpenNonExistingFile(const char* virtualRootPath);

	// Note the following tests were not ported from GVFlt:
	//
	// OpenFileForRead
	//    - Covered in RGFS.FunctionalTests.Tests.EnlistmentPerFixture.WorkingDirectoryTests.ProjectedFileHasExpectedContents
	// OpenFileForWrite
	//    - Covered in RGFS.FunctionalTests.Tests.LongRunningEnlistment.WorkingDirectoryTests.ShrinkFileContents (and other tests)
	// ReadFileAndVerifyContent
	//    - Covered in RGFS.FunctionalTests.Tests.EnlistmentPerFixture.WorkingDirectoryTests.ProjectedFileHasExpectedContents
	// WriteFileAndVerifyFileInScratch
	// OverwriteAndVerify
	//    - Does not apply: Tests that writing scratch layer does not impact backing layer contents
	// CreateNewFileInScratch
	// CreateNewFileAndWriteInScratch
	//    - Covered in RGFS.FunctionalTests.Tests.LongRunningEnlistment.WorkingDirectoryTests.ShrinkFileContents (and other tests)
}
