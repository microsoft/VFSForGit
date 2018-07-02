// GVFS.ReadObjectHook
//
// When GVFS installs GVFS.ReadObjectHook.exe, it copies the file to
// the .git\hooks folder, and renames the executable to read-object.exe
// read-object.exe is called by git.exe when it fails to find the object it's looking for on disk.
//
// Git and read-object.exe negoiate an interface and capabilities then git issues a "get" command for the missing SHA.
// See Git Documentation/Technical/read-object-protocol.txt for details.
// GVFS.ReadObjectHook decides which GVFS instance to connect to based on it's path.
// It then connects to GVFS and asks GVFS to download the requested object (to the .git\objects folder).

#include "stdafx.h"
#include "packet.h"
#include "common.h"

#define MAX_PACKET_LENGTH 512
#define SHA1_LENGTH 40
#define MESSAGE_LENGTH (4 + SHA1_LENGTH + 1)

enum ReadObjectHookErrorReturnCode
{
	ErrorReadObjectProtocol = ReturnCode::LastError + 1,
};

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
		die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad welcome message\n");
	}

	packet_txt_read(packet_buffer, sizeof(packet_buffer));
	if (strcmp(packet_buffer, "version=1"))
	{
		die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad version\n");
	}

	if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
	{
		die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad version end\n");
	}

	packet_txt_write("git-read-object-server");
	packet_txt_write("version=1");
	packet_flush();

	packet_txt_read(packet_buffer, sizeof(packet_buffer));
	if (strcmp(packet_buffer, "capability=get"))
	{
		die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad capability\n");
	}

	if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
	{
		die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad capability end\n");
	}

	packet_txt_write("capability=get");
	packet_flush();

	std::wstring pipeName(GetGVFSPipeName(argv[0]));

	HANDLE pipeHandle = CreatePipeToGVFS(pipeName);

	while (1)
	{
		packet_txt_read(packet_buffer, sizeof(packet_buffer));
		if (strcmp(packet_buffer, "command=get"))
		{
			die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad command\n");
		}

		len = packet_txt_read(packet_buffer, sizeof(packet_buffer));
		if ((len != SHA1_LENGTH + 5) || strncmp(packet_buffer, "sha1=", 5))
		{
			die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad sha1 in get command\n");
		}

		if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
		{
			die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad command end\n");
		}

		err = DownloadSHA(pipeHandle, packet_buffer + 5);
		packet_txt_write(err ? "status=error" : "status=success");
		packet_flush();
	}

	// we'll never reach here as the signal to exit is having stdin closed which is handled in packet_bin_read
}
