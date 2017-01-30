#pragma once

extern "C" 
{

NATIVE_TESTS_EXPORT bool ReadAndWriteSeparateHandles(const char* fileVirtualPath);

NATIVE_TESTS_EXPORT bool ReadAndWriteSameHandle(const char* fileVirtualPath, bool synchronousIO);

NATIVE_TESTS_EXPORT bool ReadAndWriteRepeatedly(const char* fileVirtualPath, bool synchronousIO);

NATIVE_TESTS_EXPORT bool RemoveReadOnlyAttribute(const char* fileVirtualPath);

NATIVE_TESTS_EXPORT bool CannotWriteToReadOnlyFile(const char* fileVirtualPath);

NATIVE_TESTS_EXPORT bool EnumerateAndReadDoesNotChangeEnumerationOrder(const char* folderVirtualPath);

NATIVE_TESTS_EXPORT bool EnumerationErrorsMatchNTFSForNonExistentFolder(const char* nonExistentVirtualPath, const char* nonExistentPhysicalPath);

NATIVE_TESTS_EXPORT bool EnumerationErrorsMatchNTFSForEmptyFolder(const char* emptyFolderVirtualPath, const char* emptyFolderPhysicalPath);

NATIVE_TESTS_EXPORT bool CanDeleteEmptyFolderWithFileDispositionOnClose(const char* emptyFolderPath);

NATIVE_TESTS_EXPORT bool ErrorWhenPathTreatsFileAsFolderMatchesNTFS(const char* fileVirtualPath, const char* fileNTFSPath, int creationDisposition);

}
