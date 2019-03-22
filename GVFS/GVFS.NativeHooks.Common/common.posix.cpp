#include "stdafx.h"

#include <dirent.h>
#include <errno.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <sys/un.h>
#include <unistd.h>

#include "common.h"

#define MAX_PATH 260

PATH_STRING GetFinalPathName(const PATH_STRING& path)
{
    // TODO(#1358): Implement
    return path;
}

PATH_STRING GetGVFSPipeName(const char *appName)
{
    // The pipe name is built using the path of the GVFS enlistment root.
    // Start in the current directory and walk up the directory tree
    // until we find a folder that contains the DOT_GVFS_ROOT folder
    
    // TODO 640838: Support paths longer than MAX_PATH
    char enlistmentRoot[MAX_PATH];
    if (getcwd(enlistmentRoot, MAX_PATH) == nullptr)
    {
        die(ReturnCode::GetCurrentDirectoryFailure, "getcwd failed (%d)\n", errno);
    }
    
    PATH_STRING finalRootPath(GetFinalPathName(enlistmentRoot));
    size_t enlistmentRootLength = finalRootPath.length();
    // allow an extra byte in case we need to add a trailing slash
    if (enlistmentRootLength + 2 > sizeof(enlistmentRoot))
    {
        die(ReturnCode::PipeConnectError,
            "Could not copy finalRootPath: %s, insufficient buffer. enlistmentRootLength: %zu, sizeof(enlistmentRoot): %zu\n",
            finalRootPath.c_str(),
            enlistmentRootLength,
            sizeof(enlistmentRoot));
    }
    
    memcpy(enlistmentRoot, finalRootPath.c_str(), enlistmentRootLength);
    if (enlistmentRootLength == 0 || enlistmentRoot[enlistmentRootLength - 1] != '/')
    {
        enlistmentRoot[enlistmentRootLength++] = '/';
    }
    enlistmentRoot[enlistmentRootLength] = '\0';
    
    // Walk up enlistmentRoot looking for a folder named DOT_GVFS_ROOT
    char* lastslash = enlistmentRoot + enlistmentRootLength - 1;
    bool gvfsFound = false;
    while (1)
    {
        *lastslash = '\0';
        DIR* directory = opendir(enlistmentRoot);
        if (directory == nullptr)
        {
            die(ReturnCode::NotInGVFSEnlistment, "Failed to open directory: %s, error: %d\n", enlistmentRoot, errno);
        }
        
        dirent* dirEntry = readdir(directory);
        while (!gvfsFound && dirEntry != nullptr)
        {
            if (dirEntry->d_type == DT_DIR && strcmp(dirEntry->d_name, DOT_GVFS_ROOT) == 0)
            {
                gvfsFound = true;
            }
            else
            {
                dirEntry = readdir(directory);
            }
        }
        
        closedir(directory);
        
        if (gvfsFound)
        {
            break;
        }
        
        if (errno != 0)
        {
            die(ReturnCode::NotInGVFSEnlistment, "readdir failed in directory: %s, error: %i\n", enlistmentRoot, errno);
        }
        
        lastslash--;
        while ((enlistmentRoot != lastslash) && (*lastslash != '/'))
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
    
    return PATH_STRING(enlistmentRoot) + "/" + DOT_GVFS_ROOT + "/GVFS_NetCorePipe";
}

PIPE_HANDLE CreatePipeToGVFS(const PATH_STRING& pipeName)
{
    PIPE_HANDLE socket_fd = socket(PF_UNIX, SOCK_STREAM, 0);
    if (socket_fd < 0)
    {
        die(ReturnCode::PipeConnectError, "Failed to create a new socket, pipeName: %s, error: %d\n", pipeName.c_str(), errno);
    }
    
    struct sockaddr_un socket_address;
    memset(&socket_address, 0, sizeof(struct sockaddr_un));
    
    socket_address.sun_family = AF_UNIX;
    size_t pathLength = pipeName.length();
    if (pathLength + 1 > sizeof(socket_address.sun_path))
    {
        die(ReturnCode::PipeConnectError,
            "Could not copy pipeName: %s, insufficient buffer. pathLength: %zu, sizeof(socket_address.sun_path): %zu\n",
            pipeName.c_str(),
            pathLength,
            sizeof(socket_address.sun_path));
    }
    
    memcpy(socket_address.sun_path, pipeName.c_str(), pathLength);
    socket_address.sun_path[pathLength] = '\0';

    if(connect(socket_fd, (struct sockaddr *) &socket_address, sizeof(struct sockaddr_un)) != 0)
    {
        die(ReturnCode::PipeConnectError, "Failed to connect socket, pipeName: %s, error: %d\n", pipeName.c_str(), errno);
    }
    
    return socket_fd;
}

void DisableCRLFTranslationOnStdPipes()
{
    // not required on Mac
}

bool WriteToPipe(PIPE_HANDLE pipe, const char* message, size_t messageLength, /* out */ size_t* bytesWritten, /* out */ int* error)
{

    size_t bytesRemaining = messageLength;
    while (bytesRemaining > 0)
    {
        size_t offset = messageLength - bytesRemaining;
        ssize_t bytesSent = write(pipe, message + offset, bytesRemaining);
       
        if (-1 == bytesSent)
        {
            if (EINTR != errno)
            {
                break;
            }
        }
        else
        {
            bytesRemaining -= bytesSent;
        }
    }

    *bytesWritten = messageLength - bytesRemaining;

    bool success = *bytesWritten == messageLength;
    *error = success ? 0 : errno;
    return success;
}

bool ReadFromPipe(PIPE_HANDLE pipe, char* buffer, size_t bufferLength, /* out */ size_t* bytesRead, /* out */ int* error)
{
    *error = 0;
    *bytesRead = 0;
    ssize_t readByteCount;
    
    do 
    {
        readByteCount = recv(pipe, buffer, bufferLength, 0);
    } while (readByteCount == -1 && errno == EINTR);
    
    if (readByteCount < 0)
    {
        *error = errno;
        return false;
    }
    
    *bytesRead = readByteCount;
    return true;
}
