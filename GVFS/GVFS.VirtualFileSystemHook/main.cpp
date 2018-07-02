#include "stdafx.h"
#include "common.h"

enum VirtualFileSystemErrorReturnCode
{
	ErrorVirtualFileSystemProtocol = ReturnCode::LastError + 1,
};

int main(int argc, char *argv[])
{
    if (argc != 2)
    {
        die(VirtualFileSystemErrorReturnCode::ErrorVirtualFileSystemProtocol, "Invalid arguments");
    }

    if (strcmp(argv[1], "1"))
    {
        die(VirtualFileSystemErrorReturnCode::ErrorVirtualFileSystemProtocol, "Bad version");
    }

    // set the mode to binary so we don't get CRLF translation
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);

    std::wstring pipeName(GetGVFSPipeName(argv[0]));
    HANDLE pipeHandle = CreatePipeToGVFS(pipeName);

    // Construct projection request message
    DWORD bytesWritten;
    DWORD messageLength = 6;
    BOOL success = WriteFile(
        pipeHandle,             // pipe handle 
        "MPL|1\n",              // message 
        messageLength,          // message length 
        &bytesWritten,          // bytes written 
        NULL);                  // not overlapped 

    if (!success || bytesWritten != messageLength)
    {
        die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", GetLastError());
    }

    char message[1024];
    DWORD bytesRead;
    DWORD lastError;
    BOOL finishedReading = false;
    BOOL firstRead = true;
    do
    {
        char *pMessage = &message[0];

        // Read from the pipe. 
        success = ReadFile(
            pipeHandle,         // pipe handle 
            message,            // buffer to receive reply 
            sizeof(message),    // size of buffer 
            &bytesRead,         // number of bytes read 
            NULL);              // not overlapped 

        lastError = GetLastError();
        if (!success && lastError != ERROR_MORE_DATA)
        {
            break;
        }

        messageLength = bytesRead;
        if (firstRead)
        {
            firstRead = false;
            if (message[0] != 'S')
            {
                die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%s)\n", message);
            }

            pMessage += 2;
            messageLength -= 2;
        }

        // minus 2 to remove the CRLF at the end
        if (*(pMessage + messageLength - 1) == '\n')
        {
            finishedReading = true;
            messageLength -= 1;
        }

        if (*(pMessage + messageLength - 1) == '\r')
        {
            finishedReading = true;
            messageLength -= 1;
        }

        fwrite(pMessage, 1, messageLength, stdout);

    } while (success && !finishedReading);  // repeat loop if ERROR_MORE_DATA 

    if (!success)
    {
        die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%d)\n", GetLastError());
    }

    return 0;
}

