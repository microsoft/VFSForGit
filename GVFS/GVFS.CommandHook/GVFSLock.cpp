#include "stdafx.h"
#include "common.h"
#include "Console.h"
#include "Messages.h"
#include "String.h"

using std::chrono::milliseconds;
using std::function;
using std::ostringstream;
using std::stoi;
using std::string;
using std::this_thread::sleep_for;
using std::to_string;
using std::vector;

static const string AcquireRequest = "AcquireLock";
static const string DenyGVFSResult = "LockDeniedGVFS";
static const string DenyGitResult = "LockDeniedGit";
static const string AcceptResult = "LockAcquired";
static const string AvailableResult = "LockAvailable";
static const string MountNotReadyResult = "MountNotReady";
static const string UnmountInProgressResult = "UnmountInProgress";

static bool CheckAcceptResponse(const string& responseHeader, bool checkAvailabilityOnly, string& message);
static string ParseCommandFromLockResponse(const string& responseBody);

bool GVFSLock_TryAcquireGVFSLockForProcess(
    bool unattended,
    PIPE_HANDLE pipeClient,
    const string& fullCommand,
    int pid,
    bool isElevated,
    bool isConsoleOutputRedirectedToFile,
    bool checkAvailabilityOnly,
    const string& gitCommandSessionId,
    string& result)
{
    // Format:
    // "AcquireLock|<pid>|<is elevated>|<checkAvailabilityOnly>|<parsed command length>|<parsed command>|<gitcommndsessionid length>|<gitcommand sessionid>"

    ostringstream requestMessageStream;
    requestMessageStream
        << "AcquireLock" << "|"
        << pid << "|"
        << (isElevated ? "true" : "false") << "|"
        << (checkAvailabilityOnly ? "true" : "false") << "|"
        << fullCommand.length() << "|"
        << fullCommand << "|"
        << gitCommandSessionId.length() << "|"
        << gitCommandSessionId
        << TerminatorChar;

    string requestMessage = requestMessageStream.str();

    unsigned long bytesWritten;
    unsigned long messageLength = static_cast<unsigned long>(requestMessage.length());
    int error = 0;
    bool success = WriteToPipe(
        pipeClient,
        requestMessage.c_str(),
        messageLength,
        &bytesWritten,
        &error);

    if (!success || bytesWritten != messageLength)
    {
        result = "Failed to write to pipe (" + to_string(error) + ")";
        return false;
    }

    string response;
    success = Messages_ReadTerminatedMessageFromGVFS(pipeClient, /* out */ response);

    if (!success)
    {
        result = "Failed to read response";
        return false;
    }

    size_t headerSeparator = response.find(MessageSeparator);
    string responseHeader;
    string message;
    if (headerSeparator != string::npos)
    {
        responseHeader = response.substr(0, headerSeparator);
    }
    else
    {
        responseHeader = response;
    }

    if (responseHeader == AcceptResult || responseHeader == AvailableResult)
    {
        return CheckAcceptResponse(responseHeader, checkAvailabilityOnly, result);
    }
    else if (responseHeader == MountNotReadyResult)
    {
        result = "GVFS has not finished initializing, please wait a few seconds and try again.";
        return false;
    }
    else if (responseHeader == UnmountInProgressResult)
    {
        result = "GVFS is unmounting.";
        return false;
    }
    else if (responseHeader == DenyGVFSResult)
    {
        message = response.substr(headerSeparator + 1);
    }
    else if (responseHeader == DenyGitResult)
    {
        message = "Waiting for '" + ParseCommandFromLockResponse(response.substr(headerSeparator + 1)) + "' to release the lock";
    }
    else
    {
        result = "Error when acquiring the lock. Unrecognized response: " + response;
        return false;
    }

    auto waitForLock = [pipeClient, &requestMessage, &result, checkAvailabilityOnly]()
    {
        while (true)
        {
            sleep_for(milliseconds(250));

            unsigned long bytesWritten;
            unsigned long messageLength = static_cast<unsigned long>(requestMessage.length());
            int error = 0;
            bool success = WriteToPipe(
                pipeClient,
                requestMessage.c_str(),
                messageLength,
                &bytesWritten,
                &error);

            if (!success || bytesWritten != messageLength)
            {
                die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", error);
            }

            string response;
            success = Messages_ReadTerminatedMessageFromGVFS(pipeClient, /* out */ response);

            if (!success)
            {
                result = "Failed to read response";
                return false;
            }

            size_t headerSeparator = response.find(MessageSeparator);
            string responseHeader;
            if (headerSeparator != string::npos)
            {
                responseHeader = response.substr(0, headerSeparator);
            }
            else
            {
                responseHeader = response;
            }

            if (responseHeader == AcceptResult || responseHeader == AvailableResult)
            {
                return CheckAcceptResponse(responseHeader, checkAvailabilityOnly, result);
            }
            else if (responseHeader == UnmountInProgressResult)
            {
                return false;
            }
        }
    };

    bool isSuccessfulLockResult;
    if (unattended)
    {
        isSuccessfulLockResult = waitForLock();
    }
    else
    {
        isSuccessfulLockResult = Console_ShowStatusWhileRunning(
            waitForLock,
            message,
            !isConsoleOutputRedirectedToFile, // showSpinner
            0); // initialDelayMs
    }

    result = "";
    return isSuccessfulLockResult;
}

static bool CheckAcceptResponse(const string& responseHeader, bool checkAvailabilityOnly, string& message)
{
    if (responseHeader == AcceptResult)
    {
        if (!checkAvailabilityOnly)
        {
            message = "";
            return true;
        }
        else
        {
            message = "Error when acquiring the lock. Unexpected response: "; // +response.CreateMessage();
            return false;
        }
    }
    else if (responseHeader == AvailableResult)
    {
        if (checkAvailabilityOnly)
        {
            message = "";
            return true;
        }
        else
        {
            message = "Error when acquiring the lock. Unexpected response: "; // +response.CreateMessage();
            return false;
        }
    }

    message = "Error when acquiring the lock. Not an Accept result: "; // +response.CreateMessage();
    return false;
}

static string ParseCommandFromLockResponse(const string& responseBody)
{
    if (!responseBody.empty())
    {
        // This mesage is stored using the MessageSeperator delimiter for performance reasons
        // Format of the body uses length prefixed string so that the strings can have the delimiter in them
        // Examples:
        // "123|true|false|13|parsedCommand|9|sessionId"
        // "321|false|true|30|parsedCommand with | delimiter|26|sessionId with | delimiter"
        vector<string> dataParts(String_Split(MessageSeparator, responseBody));
        if (dataParts.size() < 7)
        {
            die(ReturnCode::InvalidResponse, "Invalid lock message. Expected at least 7 parts, got: %zu from message: '%s'", dataParts.size(), responseBody.c_str());
        }

        int parsedCommandLength = 0;
        try
        {
            parsedCommandLength = stoi(dataParts[3]);
        }
        catch (...)
        {
            die(ReturnCode::InvalidResponse, "Invalid lock message. Failed to parse command length: '%s'", dataParts[3].c_str());
        }

        // ParsedCommandLength should be the length of the string at the end of the message
        // Add the length of the previous parts, plus delimiters
        size_t commandStartingSpot = dataParts[0].length() + dataParts[1].length() + dataParts[2].length() + dataParts[3].length() + 4;
        if ((commandStartingSpot + parsedCommandLength) >= responseBody.length())
        {
            die(ReturnCode::InvalidResponse, "Invalid lock message. The parsedCommand is an unexpected length, got: {0} from message: '{1}'", parsedCommandLength, responseBody.c_str());
        }

        return responseBody.substr(commandStartingSpot, parsedCommandLength);
    }

    return "";
}