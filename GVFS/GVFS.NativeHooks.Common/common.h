#pragma once

enum ReturnCode
{
	Success = 0,
	InvalidArgCount = 1,
	GetCurrentDirectoryFailure = 2,
	NotInGVFSEnlistment = 3,
	PipeConnectError = 4,
	PipeConnectTimeout = 5,
	InvalidSHA = 6,
	PipeWriteFailed = 7,
	PipeReadFailed = 8,
	FailureToDownload = 9,
	PathNameError = 10,

	LastError = PathNameError,	
};

inline void die(int err, const char *fmt, ...)
{
	va_list params;
	va_start(params, fmt);
	vfprintf(stderr, fmt, params);
	va_end(params);
	exit(err);
}

inline std::wstring GetFinalPathName(const std::wstring& path)
{
	HANDLE fileHandle;

	// Using FILE_FLAG_BACKUP_SEMANTICS as it works with file as well as folder path
	// According to MSDN, https://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx,
	// we must set this flag to obtain a handle to a directory
	fileHandle = CreateFileW(
		path.c_str(),
		FILE_READ_ATTRIBUTES,
		FILE_SHARE_READ | FILE_SHARE_WRITE,
		NULL,
		OPEN_EXISTING,
		FILE_FLAG_BACKUP_SEMANTICS,
		NULL);

	if (fileHandle == INVALID_HANDLE_VALUE)
	{
		die(ReturnCode::PathNameError, "Could not open oppen handle to %ls to determine final path name, Error: %d\n", path.c_str(), GetLastError());
	}

	wchar_t finalPathByHandle[MAX_PATH] = { 0 };
	DWORD finalPathSize = GetFinalPathNameByHandleW(fileHandle, finalPathByHandle, MAX_PATH, FILE_NAME_NORMALIZED);
	if (finalPathSize == 0)
	{
		die(ReturnCode::PathNameError, "Could not get final path name by handle for %ls, Error: %d\n", path.c_str(), GetLastError());
	}

	std::wstring finalPath(finalPathByHandle);

	// The remarks section of GetFinalPathNameByHandle mentions the return being prefixed with "\\?\" or "\\?\UNC\"
	// More information the prefixes is here http://msdn.microsoft.com/en-us/library/aa365247(v=VS.85).aspx
	std::wstring PathPrefix(L"\\\\?\\");
	std::wstring UncPrefix(L"\\\\?\\UNC\\");

	if (finalPath.compare(0, UncPrefix.length(), UncPrefix) == 0)
	{
		finalPath = L"\\\\" + finalPath.substr(UncPrefix.length());
	}
	else if (finalPath.compare(0, PathPrefix.length(), PathPrefix) == 0)
	{
		finalPath = finalPath.substr(PathPrefix.length());
	}

	return finalPath;
}

inline std::wstring GetGVFSPipeName(const char *appName)
{
	// The pipe name is build using the path of the GVFS enlistment root.
	// Start in the current directory and walk up the directory tree
	// until we find a folder that contains the ".gvfs" folder

	const size_t dotGVFSRelativePathLength = sizeof(L"\\.gvfs") / sizeof(wchar_t);

	// TODO 640838: Support paths longer than MAX_PATH
	wchar_t enlistmentRoot[MAX_PATH];
	DWORD currentDirResult = GetCurrentDirectoryW(MAX_PATH - dotGVFSRelativePathLength, enlistmentRoot);
	if (currentDirResult == 0 || currentDirResult > MAX_PATH - dotGVFSRelativePathLength)
	{
		die(ReturnCode::GetCurrentDirectoryFailure, "GetCurrentDirectory failed (%d)\n", GetLastError());
	}

	std::wstring finalRootPath(GetFinalPathName(enlistmentRoot));
	errno_t copyResult = wcscpy_s(enlistmentRoot, finalRootPath.c_str());
	if (copyResult != 0)
	{
		die(ReturnCode::PipeConnectError, "Could not copy finalRootPath: %ls. Error: %d\n", finalRootPath.c_str(), copyResult);
	}

	size_t enlistmentRootLength = wcslen(enlistmentRoot);
	if ('\\' != enlistmentRoot[enlistmentRootLength - 1])
	{
		wcscat_s(enlistmentRoot, L"\\");
		enlistmentRootLength++;
	}

	// Walk up enlistmentRoot looking for a folder named .gvfs
	wchar_t* lastslash = enlistmentRoot + enlistmentRootLength - 1;
	WIN32_FIND_DATAW findFileData;
	HANDLE dotGVFSHandle;
	while (1)
	{
		wcscat_s(lastslash, MAX_PATH - (lastslash - enlistmentRoot), L".gvfs");
		dotGVFSHandle = FindFirstFileW(enlistmentRoot, &findFileData);
		if (dotGVFSHandle != INVALID_HANDLE_VALUE)
		{
			FindClose(dotGVFSHandle);
			if (findFileData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
			{
				break;
			}
		}

		lastslash--;
		while ((enlistmentRoot != lastslash) && (*lastslash != '\\'))
		{
			lastslash--;
		}

		if (enlistmentRoot == lastslash)
		{
			die(ReturnCode::NotInGVFSEnlistment, "%s must be run from inside a GVFS enlistment\n", appName);
		}

		*(lastslash + 1) = 0;
	};

	*(lastslash) = 0;

	std::wstring namedPipe(CharUpperW(enlistmentRoot));
	std::replace(namedPipe.begin(), namedPipe.end(), L':', L'_');
	return L"\\\\.\\pipe\\GVFS_" + namedPipe;
}

inline HANDLE CreatePipeToGVFS(const std::wstring& pipeName)
{
	HANDLE pipeHandle;
	while (1)
	{
		pipeHandle = CreateFileW(
			pipeName.c_str(), // pipe name 
			GENERIC_READ |     // read and write access 
			GENERIC_WRITE,
			0,                 // no sharing 
			NULL,              // default security attributes
			OPEN_EXISTING,     // opens existing pipe 
			0,                 // default attributes 
			NULL);             // no template file 

		if (pipeHandle != INVALID_HANDLE_VALUE)
		{
			break;
		}

		if (GetLastError() != ERROR_PIPE_BUSY)
		{
			die(ReturnCode::PipeConnectError, "Could not open pipe: %ls, Error: %d\n", pipeName.c_str(), GetLastError());
		}

		if (!WaitNamedPipeW(pipeName.c_str(), 3000))
		{
			die(ReturnCode::PipeConnectTimeout, "Could not open pipe: %ls, Timed out.", pipeName.c_str());
		}
	}

	return pipeHandle;
}