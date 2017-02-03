// GVFS.ReadObjectHook
//
// GVFS.ReadObjectHook connects to GVFS and asks GVFS to download a git object (to the .git\objects folder).
// GVFS.ReadObjectHook accepts the desired object SHA as an argument, and
// decides which GVFS instance to connect to based on GVFS.ReadObjectHook.exe's
// current working directory.
//
// When GVFS installs GVFS.ReadObjectHook.exe, it copies the file to
// the .git\hooks folder, and renames the executable to read-object.exe
// read-object.exe is called by git.exe when it fails to find the object it's looking for on disk.

#include "stdafx.h"

#define REC_BUF_SIZE 512
#define MAX_REQUEST_CHARS 128

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
    PipeReadFailed = 8
};

std::wstring GetGVFSPipeName(wchar_t* appName);
HANDLE CreatePipeToGVFS(const std::wstring& pipeName);

int wmain(int argc, WCHAR *argv[])
{
    if (argc != 2)
    {
        fwprintf(stderr, L"Usage: %s <object sha>\n", argv[0]);
        exit(ReturnCode::InvalidArgCount);
    }
    
    // Construct download request message
    // Format:  "DLO|<40 character SHA>"
    // Example: "DLO|920C34DCDDFC8F07AC4704C8C0D087D6F2095729"
    wchar_t message[MAX_REQUEST_CHARS];
    if (_snwprintf_s(message, _TRUNCATE, L"DLO|%s", argv[1]) < 0)
    {
        fwprintf(stderr, L"First argument must be a 40 character SHA, actual value: %s\n", argv[1]);
        exit(ReturnCode::InvalidSHA);
    }

    std::wstring pipeName(GetGVFSPipeName(argv[0]));

    HANDLE pipeHandle = CreatePipeToGVFS(pipeName);
    
    // GVFS expects message in UTF8 format
    std::wstring_convert<std::codecvt_utf8<wchar_t>> utf8Converter;
    std::string utf8message(utf8Converter.to_bytes(message));
    utf8message += "\n";

    DWORD bytesWritten;
    BOOL success = WriteFile(
        pipeHandle,                               // pipe handle 
        utf8message.c_str(),                      // message 
        (static_cast<DWORD>(utf8message.size())), // message length 
        &bytesWritten,                            // bytes written 
        NULL);                                    // not overlapped 

    if (!success)
    {
        fwprintf(stderr, L"Failed to write to pipe (%d)\n", GetLastError());
        CloseHandle(pipeHandle);
        exit(ReturnCode::PipeWriteFailed);
    }

    DWORD bytesRead;
    wchar_t receiveBuffer[REC_BUF_SIZE];
    do
    {
        // Read from the pipe. 
        success = ReadFile(
            pipeHandle,                     // pipe handle 
            receiveBuffer,                  // buffer to receive reply 
            REC_BUF_SIZE * sizeof(wchar_t), // size of buffer 
            &bytesRead,                     // number of bytes read 
            NULL);                          // not overlapped 

        if (!success && GetLastError() != ERROR_MORE_DATA)
        {
            break;
        }
    } while (!success);  // repeat loop if ERROR_MORE_DATA 

    CloseHandle(pipeHandle);

    if (!success)
    {
        fwprintf(stderr, L"Read response from pipe failed (%d)\n", GetLastError());        
        exit(ReturnCode::PipeReadFailed);
    }

    // Treat the hook as successful regardless of the contents of receiveBuffer
    // if GVFS did not download the object, then git.exe will see that it's missing when
    // it attempts to read from the object again

    return ReturnCode::Success;
}

inline std::wstring GetGVFSPipeName(wchar_t* appName)
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
        fwprintf(stderr, L"GetCurrentDirectory failed (%d)\n", GetLastError());
        exit(ReturnCode::GetCurrentDirectoryFailure);
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
            fwprintf(stderr, L"%s must be run from inside a GVFS enlistment\n", appName);
            exit(ReturnCode::NotInGVFSEnlistment);
        }
        *(lastslash + 1) = 0;
    };

    *(lastslash) = 0;

    std::wstring namedPipe(CharUpper(enlistmentRoot));
    std::replace(namedPipe.begin(), namedPipe.end(), L':', L'_');
    return L"\\\\.\\pipe\\GVFS_" + namedPipe;
}

inline HANDLE CreatePipeToGVFS(const std::wstring& pipeName)
{
    HANDLE pipeHandle;
    while (1)
    {
        pipeHandle = CreateFile(
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
            fwprintf(stderr, L"Could not open pipe. (%d)\n", GetLastError());
            exit(ReturnCode::PipeConnectError);
        }

        if (!WaitNamedPipe(pipeName.c_str(), 3000))
        {
            fwprintf(stderr, L"Could not open pipe: Timed out.");
            exit(ReturnCode::PipeConnectTimeout);
        }
    }

    return pipeHandle;
}
