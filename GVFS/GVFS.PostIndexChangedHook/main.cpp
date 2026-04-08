#include "stdafx.h"
#include "common.h"

enum PostIndexChangedErrorReturnCode
{
    ErrorPostIndexChangedProtocol = ReturnCode::LastError + 1,
};

const int PIPE_BUFFER_SIZE = 1024;

// Returns true if GIT_INDEX_FILE refers to a non-canonical (temp) index.
// The canonical index path is $GIT_DIR/index; anything else is a temp
// index that GVFS doesn't need to be notified about.
//
// GIT_DIR is always set by git.exe itself (via xsetenv in setup.c) before
// any hook runs, so it is reliably present. GIT_INDEX_FILE is only present
// when an external caller (script, build tool, etc.) explicitly exports it
// before invoking git, to redirect index operations to a temp file.
static bool IsNonCanonicalIndex()
{
    char *indexFileEnv = NULL;
    size_t indexLen = 0;
    _dupenv_s(&indexFileEnv, &indexLen, "GIT_INDEX_FILE");

    if (indexFileEnv == NULL || indexFileEnv[0] == '\0')
    {
        free(indexFileEnv);
        return false;
    }

    char *gitDirEnv = NULL;
    size_t gitDirLen = 0;
    _dupenv_s(&gitDirEnv, &gitDirLen, "GIT_DIR");

    if (gitDirEnv == NULL || gitDirEnv[0] == '\0')
    {
        // GIT_INDEX_FILE is set but GIT_DIR is not — shouldn't happen
        // inside a hook (git.exe always sets GIT_DIR), but err on the
        // side of correctness: proceed with the notification.
        free(indexFileEnv);
        free(gitDirEnv);
        return false;
    }

    // Build the canonical index path: <GIT_DIR>/index
    std::string canonical(gitDirEnv);
    if (!canonical.empty() && canonical.back() != '\\' && canonical.back() != '/')
        canonical += '\\';
    canonical += "index";

    // Resolve both paths to absolute form so that relative GIT_DIR
    // (e.g. ".git") and absolute GIT_INDEX_FILE compare correctly.
    char canonicalFull[MAX_PATH];
    char actualFull[MAX_PATH];
    DWORD canonLen = GetFullPathNameA(canonical.c_str(), MAX_PATH, canonicalFull, NULL);
    DWORD actualLen = GetFullPathNameA(indexFileEnv, MAX_PATH, actualFull, NULL);

    free(indexFileEnv);
    free(gitDirEnv);

    if (canonLen == 0 || canonLen >= MAX_PATH ||
        actualLen == 0 || actualLen >= MAX_PATH)
    {
        // Path resolution failed — err on the side of correctness.
        return false;
    }

    return _stricmp(actualFull, canonicalFull) != 0;
}

int main(int argc, char *argv[])
{
    if (argc != 3)
    {
        die(ReturnCode::InvalidArgCount, "Invalid arguments");
    }

    // Skip notification for non-canonical (temp) index files.
    // Git fires post-index-change for every index write, including temp
    // indexes created via GIT_INDEX_FILE redirect (e.g. read-tree
    // --index-output, git add with a temp index). GVFS only needs to
    // know about changes to the real $GIT_DIR/index.
    if (IsNonCanonicalIndex())
    {
        return 0;
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

