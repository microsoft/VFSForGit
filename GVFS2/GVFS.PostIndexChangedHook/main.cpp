#include "stdafx.h"
#include "common.h"

enum PostIndexChangedErrorReturnCode
{
    ErrorPostIndexChangedProtocol = ReturnCode::LastError + 1,
};

const int PIPE_BUFFER_SIZE = 1024;

int main(int argc, char *argv[])
{
    if (argc != 3)
    {
        die(ReturnCode::InvalidArgCount, "Invalid arguments");
    }

    if (strcmp(argv[1], "1") && strcmp(argv[1], "0"))
    {
        die(PostIndexChangedErrorReturnCode::ErrorPostIndexChangedProtocol, "Invalid value passed for first argument");
    }

    if (strcmp(argv[2], "1") && strcmp(argv[2], "0"))
    {
        die(PostIndexChangedErrorReturnCode::ErrorPostIndexChangedProtocol, "Invalid value passed for second argument");
    }

    DisableCRLFTranslationOnStdPipes();

    PATH_STRING pipeName(GetGVFSPipeName(argv[0]));
    PIPE_HANDLE pipeHandle = CreatePipeToGVFS(pipeName);

    // Construct index changed request message
    // Format:  "PICN|<working directory updated flag><skipworktree bits updated flag>"
    // Example: "PICN|10"
    // Example: "PICN|01"
    // Example: "PICN|00"
    unsigned long bytesWritten;
    const unsigned long messageLength = 8;
    int error = 0;
    char request[messageLength];
    if (snprintf(request, messageLength, "PICN|%s%s", argv[1], argv[2]) != messageLength - 1)
    {
        die(PostIndexChangedErrorReturnCode::ErrorPostIndexChangedProtocol, "Invalid value for message");
    }

    request[messageLength - 1] = 0x03;
    bool success = WriteToPipe(
        pipeHandle,
        request,
        messageLength,
        &bytesWritten,
        &error);

    if (!success || bytesWritten != messageLength)
    {
        die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", error);
    }

    // Allow for 1 extra character in case we need to
    // null terminate the message, and the message
    // is PIPE_BUFFER_SIZE chars long.
    char message[PIPE_BUFFER_SIZE + 1];
    unsigned long bytesRead;
    int lastError;
    success = ReadFromPipe(
        pipeHandle,
        message,
        PIPE_BUFFER_SIZE,
        &bytesRead,
        &lastError);

    if (!success)
    {
        die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%d)\n", lastError);
    }

    if (message[0] != 'S')
    {
        die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%s)\n", message);
    }

    return 0;
}

