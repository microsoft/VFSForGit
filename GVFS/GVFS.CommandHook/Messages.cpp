#include "stdafx.h"
#include "common.h"
#include "Messages.h"

using std::ostringstream;
using std::string;

static const int PIPE_BUFFER_SIZE = 1024;

bool Messages_ReadTerminatedMessageFromGVFS(PIPE_HANDLE pipeHandle, string& responseMessage)
{
    // Allow for 1 extra character in case we need to
    // null terminate the message, and the message
    // is PIPE_BUFFER_SIZE chars long.
    char message[PIPE_BUFFER_SIZE + 1];
    unsigned long bytesRead;
    unsigned long messageLength;
    int lastError;
    bool finishedReading = false;
    bool success;
    ostringstream response;

    do
    {
        success = ReadFromPipe(
            pipeHandle,
            message,
            PIPE_BUFFER_SIZE,
            &bytesRead,
            &lastError);

        if (!success)
        {
            break;
        }

        messageLength = bytesRead;

        if (message[messageLength - 1] == '\x3')
        {
            finishedReading = true;
            messageLength -= 1;
        }

        message[messageLength] = '\0';
        response << message;

    } while (success && !finishedReading);

    if (!success)
    {
        return false;
    }

    responseMessage = response.str();
    return true;
}