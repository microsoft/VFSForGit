// GVFS.ReadObjectHook
//
// When GVFS installs GVFS.ReadObjectHook, it copies the file to
// the .git\hooks folder, and renames the executable to read-object
// read-object is called by git when it fails to find the object it's looking for on disk.
//
// Git and read-object negotiate an interface and capabilities then git issues a "get" command for the missing SHA.
// See Git Documentation/Technical/read-object-protocol.txt for details.
// GVFS.ReadObjectHook decides which GVFS instance to connect to based on its path.
// It then connects to GVFS and asks GVFS to download the requested object (to the .git\objects folder).

#include "stdafx.h"
#include "packet.h"
#include "common.h"

#define MAX_PACKET_LENGTH 512
#define SHA1_LENGTH 40
#define DLO_REQUEST_LENGTH (4 + SHA1_LENGTH + 1)

// Expected response:
// "S\x3" -> Success
// "F\x3" -> Failure
#define DLO_RESPONSE_LENGTH 2

enum ReadObjectHookErrorReturnCode
{
    ErrorReadObjectProtocol = ReturnCode::LastError + 1,
};

int DownloadSHA(PIPE_HANDLE pipeHandle, const char *sha1)
{
    // Construct download request message
    // Format:  "DLO|<40 character SHA>"
    // Example: "DLO|920C34DCDDFC8F07AC4704C8C0D087D6F2095729"
    char request[DLO_REQUEST_LENGTH+1];
    if (snprintf(request, DLO_REQUEST_LENGTH+1, "DLO|%s\x3", sha1) != DLO_REQUEST_LENGTH)
    {
        die(ReturnCode::InvalidSHA, "First argument must be a 40 character SHA, actual value: %s\n", sha1);
    }

    unsigned long bytesWritten;
    int error = 0;
    bool success = WriteToPipe(
        pipeHandle,
        request,
        DLO_REQUEST_LENGTH,
        &bytesWritten,
        &error);

    if (!success || bytesWritten != DLO_REQUEST_LENGTH)
    {
        die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", error);
    }

    char response[DLO_RESPONSE_LENGTH];
    unsigned long totalBytesRead = 0;
    error = 0;
    do
    {
        unsigned long bytesRead = 0;
        success = ReadFromPipe(
            pipeHandle,
            response + totalBytesRead,
            sizeof(response) - (sizeof(char) * totalBytesRead),
            &bytesRead,
            &error);
        totalBytesRead += bytesRead;
    } while (success && totalBytesRead < DLO_RESPONSE_LENGTH);
    
    if (!success)
    {
        die(ReturnCode::PipeReadFailed, "Read response from pipe failed (%d)\n", error);
    }

    return *response == 'S' ? ReturnCode::Success : ReturnCode::FailureToDownload;
}

int main(int, char *argv[])
{
    char packet_buffer[MAX_PACKET_LENGTH];
    size_t len;
    int err;

    DisableCRLFTranslationOnStdPipes();

    packet_txt_read(packet_buffer, sizeof(packet_buffer));
    if (strcmp(packet_buffer, "git-read-object-client")) // CodeQL [SM01932] `packet_txt_read()` either NUL-terminates or `die()`s
    {
        die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad welcome message\n");
    }

    packet_txt_read(packet_buffer, sizeof(packet_buffer));
    if (strcmp(packet_buffer, "version=1")) // CodeQL [SM01932] `packet_txt_read()` either NUL-terminates or `die()`s
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
    if (strcmp(packet_buffer, "capability=get")) // CodeQL [SM01932] `packet_txt_read()` either NUL-terminates or `die()`s
    {
        die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad capability\n");
    }

    if (packet_txt_read(packet_buffer, sizeof(packet_buffer)))
    {
        die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad capability end\n");
    }

    packet_txt_write("capability=get");
    packet_flush();

    PATH_STRING pipeName(GetGVFSPipeName(argv[0]));

    PIPE_HANDLE pipeHandle = CreatePipeToGVFS(pipeName);

    while (1)
    {
        packet_txt_read(packet_buffer, sizeof(packet_buffer));
        if (strcmp(packet_buffer, "command=get")) // CodeQL [SM01932] `packet_txt_read()` either NUL-terminates or `die()`s
        {
            die(ReadObjectHookErrorReturnCode::ErrorReadObjectProtocol, "Bad command\n");
        }

        len = packet_txt_read(packet_buffer, sizeof(packet_buffer));
        if ((len != SHA1_LENGTH + 5) || strncmp(packet_buffer, "sha1=", 5)) // CodeQL [SM01932] `packet_txt_read()` either NUL-terminates or `die()`s
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
