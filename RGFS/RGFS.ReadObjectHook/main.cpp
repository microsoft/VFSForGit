// RGFS.ReadObjectHook
//
// When RGFS installs RGFS.ReadObjectHook.exe, it copies the file to
// the .git\hooks folder, and renames the executable to read-object.exe
// read-object.exe is called by git.exe when it fails to find the object it's looking for on disk.
//
// Git and read-object.exe negoiate an interface and capabilities then git issues a "get" command for the missing SHA.
// See Git Documentation/Technical/read-object-protocol.txt for details.
// RGFS.ReadObjectHook decides which RGFS instance to connect to based on it's path.
// It then connects to RGFS and asks RGFS to download the requested object (to the .git\objects folder).

#include "stdafx.h"
#include "packet.h"

#define MAX_PACKET_LENGTH 512
#define SHA1_LENGTH 40
#define MESSAGE_LENGTH (4 + SHA1_LENGTH + 1)

enum ReturnCode
{
    Success = 0,
    InvalidArgCount = 1,
    GetCurrentDirectoryFailure = 2,
    NotInRGFSEnlistment = 3,
    PipeConnectError = 4,
    PipeConnectTimeout = 5,
    InvalidSHA = 6,
    PipeWriteFailed = 7,
    PipeReadFailed = 8,
	FailureToDownload = 9,
	ErrorReadObjectProtocol = 10
};

inline std::wstring GetRGFSPipeName(const char *appName)
{
	// The pipe name is build using the path of the RGFS enlistment root.
	// Start in the current directory and walk up the directory tree
	// until we find a folder that contains the ".rgfs" folder

	const size_t dotRGFSRelativePathLength = sizeof(L"\\.rgfs") / sizeof(wchar_t);

	// TODO 640838: Support paths longer than MAX_PATH
	wchar_t enlistmentRoot[MAX_PATH];
	DWORD currentDirResult = GetCurrentDirectoryW(MAX_PATH - dotRGFSRelativePathLength, enlistmentRoot);
	if (currentDirResult == 0 || currentDirResult > MAX_PATH - dotRGFSRelativePathLength)
	{
		die(ReturnCode::GetCurrentDirectoryFailure, "GetCurrentDirectory failed (%d)\n", GetLastError());
	}

	size_t enlistmentRootLength = wcslen(enlistmentRoot);
	if ('\\' != enlistmentRoot[enlistmentRootLength - 1])
	{
		wcscat_s(enlistmentRoot, L"\\");
		enlistmentRootLength++;
	}

	// Walk up enlistmentRoot looking for a folder named .rgfs
	wchar_t* lastslash = enlistmentRoot + enlistmentRootLength - 1;
	WIN32_FIND_DATAW findFileData;
	HANDLE dotRGFSHandle;
	while (1)
	{
		wcscat_s(lastslash, MAX_PATH - (lastslash - enlistmentRoot), L".rgfs");
		dotRGFSHandle = FindFirstFileW(enlistmentRoot, &findFileData);
		if (dotRGFSHandle != INVALID_HANDLE_VALUE)
		{
			FindClose(dotRGFSHandle);
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
			die(ReturnCode::NotInRGFSEnlistment, "%s must be run from inside a RGFS enlistment\n", appName);
		}

		*(lastslash + 1) = 0;
	};

	*(lastslash) = 0;

	std::wstring namedPipe(CharUpperW(enlistmentRoot));
	std::replace(namedPipe.begin(), namedPipe.end(), L':', L'_');
	return L"\\\\.\\pipe\\RGFS_" + namedPipe;
}

inline HANDLE CreatePipeToRGFS(const std::wstring& pipeName)
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

int DownloadSHA(HANDLE pipeHandle, const char *sha1)
{
	// Construct download request message
	// Format:  "DLO|<40 character SHA>"
	// Example: "DLO|920C34DCDDFC8F07AC4704C8C0D087D6F2095729"
	char message[MESSAGE_LENGTH+1];
	if (_snprintf_s(message, _TRUNCATE, "DLO|%s\n", sha1) < 0)
	{
		die(ReturnCode::InvalidSHA, "First argument must be a 40 character SHA, actual value: %s\n", sha1);
	}

	DWORD bytesWritten;
	BOOL success = WriteFile(
		pipeHandle,             // pipe handle 
		message,				// message 
		MESSAGE_LENGTH,			// message length 
		&bytesWritten,          // bytes written 
		NULL);                  // not overlapped 

	if (!success || bytesWritten != MESSAGE_LENGTH)
	{
		die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", GetLastError());
	}

	DWORD bytesRead;
	do
	{
		// Read from the pipe. 
		success = ReadFile(
			pipeHandle,			// pipe handle 
			message,			// buffer to receive reply 
			sizeof(message),	// size of buffer 
			&bytesRead,         // number of bytes read 
			NULL);              // not overlapped 

		if (!success && GetLastError() != ERROR_MORE_DATA)
		{
			break;
		}
	} while (!success);  // repeat loop if ERROR_MORE_DATA 
	if (!success)
	{
		die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%d)\n", GetLastError());
	}

	return *message == 'S' ? ReturnCode::Success : ReturnCode::FailureToDownload;
}

int main(int, char *argv[])
{
	char packet_buffer[MAX_PACKET_LENGTH];
	size_t len;
	int err;

	// set the mode to binary so we don't get CRLF translation
	_setmode(_fileno(stdin), _O_BINARY);
	_setmode(_fileno(stdout), _O_BINARY);

	packet_txt_read(packet_buffer, sizeof(packet_buffer));
	if (strcmp(packet_buffer, "git-read-object-client"))
	{
		die(ReturnCode::ErrorReadObjectProtocol, "Bad welcome message\n");
	}

	packet_txt_read(packet_buffer, sizeof(packet_buffer));
	if (strcmp(packet_buffer, "version=1"))
	{
		die(ReturnCode::ErrorReadObjectProtocol, "Bad version\n");
	}

	if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
	{
		die(ReturnCode::ErrorReadObjectProtocol, "Bad version end\n");
	}

	packet_txt_write("git-read-object-server");
	packet_txt_write("version=1");
	packet_flush();

	packet_txt_read(packet_buffer, sizeof(packet_buffer));
	if (strcmp(packet_buffer, "capability=get"))
	{
		die(ReturnCode::ErrorReadObjectProtocol, "Bad capability\n");
	}

	if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
	{
		die(ReturnCode::ErrorReadObjectProtocol, "Bad capability end\n");
	}

	packet_txt_write("capability=get");
	packet_flush();

	std::wstring pipeName(GetRGFSPipeName(argv[0]));

	HANDLE pipeHandle = CreatePipeToRGFS(pipeName);

	while (1)
	{
		packet_txt_read(packet_buffer, sizeof(packet_buffer));
		if (strcmp(packet_buffer, "command=get"))
		{
			die(ReturnCode::ErrorReadObjectProtocol, "Bad command\n");
		}

		len = packet_txt_read(packet_buffer, sizeof(packet_buffer));
		if ((len != SHA1_LENGTH + 5) || strncmp(packet_buffer, "sha1=", 5))
		{
			die(ReturnCode::ErrorReadObjectProtocol, "Bad sha1 in get command\n");
		}

		if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
		{
			die(ReturnCode::ErrorReadObjectProtocol, "Bad command end\n");
		}

		err = DownloadSHA(pipeHandle, packet_buffer + 5);
		packet_txt_write(err ? "status=error" : "status=success");
		packet_flush();
	}

	// we'll never reach here as the signal to exit is having stdin closed which is handled in packet_bin_read
}
