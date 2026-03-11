#pragma once
#include "stdafx.h"
#include <fcntl.h>
#include <io.h>
#include <algorithm>
#include "common.h"

PATH_STRING GetFinalPathName(const PATH_STRING& path)
{
    HANDLE fileHandle;

    // Using FILE_FLAG_BACKUP_SEMANTICS as it works with file as well as folder path
    // According to MSDN, https://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx,
    // we must set this flag to obtain a handle to a directory
    fileHandle = CreateFileW(
        path.c_str(),
        FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (fileHandle == INVALID_HANDLE_VALUE)
    {
        die(ReturnCode::PathNameError, "Could not open oppen handle to %ls to determine final path name, Error: %d\n", path.c_str(), GetLastError());
    }

    wchar_t finalPathByHandle[MAX_PATH] = { 0 };
    DWORD finalPathSize = GetFinalPathNameByHandleW(fileHandle, finalPathByHandle, MAX_PATH, FILE_NAME_NORMALIZED);
    if (finalPathSize == 0)
    {
        die(ReturnCode::PathNameError, "Could not get final path name by handle for %ls, Error: %d\n", path.c_str(), GetLastError());
    }

    std::wstring finalPath(finalPathByHandle);

    // The remarks section of GetFinalPathNameByHandle mentions the return being prefixed with "\\?\" or "\\?\UNC\"
    // More information the prefixes is here http://msdn.microsoft.com/en-us/library/aa365247(v=VS.85).aspx
    std::wstring PathPrefix(L"\\\\?\\");
    std::wstring UncPrefix(L"\\\\?\\UNC\\");

    if (finalPath.compare(0, UncPrefix.length(), UncPrefix) == 0)
    {
        finalPath = L"\\\\" + finalPath.substr(UncPrefix.length());
    }
    else if (finalPath.compare(0, PathPrefix.length(), PathPrefix) == 0)
    {
        finalPath = finalPath.substr(PathPrefix.length());
    }

    return finalPath;
}

// Checks if the given directory is a git worktree by looking for a
// ".git" file (not directory). If found, reads it to extract the
// worktree name and returns a pipe name suffix like "_WT_NAME".
// Returns an empty string if not in a worktree.
PATH_STRING GetWorktreePipeSuffix(const wchar_t* directory)
{
    wchar_t dotGitPath[MAX_PATH];
    wcscpy_s(dotGitPath, directory);
    size_t checkLen = wcslen(dotGitPath);
    if (checkLen > 0 && dotGitPath[checkLen - 1] != L'\\')
        wcscat_s(dotGitPath, L"\\");
    wcscat_s(dotGitPath, L".git");

    DWORD dotGitAttrs = GetFileAttributesW(dotGitPath);
    if (dotGitAttrs == INVALID_FILE_ATTRIBUTES ||
        (dotGitAttrs & FILE_ATTRIBUTE_DIRECTORY))
    {
        return PATH_STRING();
    }

    // .git is a file — this is a worktree. Read it to find the
    // worktree git directory (format: "gitdir: <path>")
    FILE* gitFile = NULL;
    errno_t fopenResult = _wfopen_s(&gitFile, dotGitPath, L"r");
    if (fopenResult != 0 || gitFile == NULL)
        return PATH_STRING();

    char gitdirLine[MAX_PATH * 2];
    if (fgets(gitdirLine, sizeof(gitdirLine), gitFile) == NULL)
    {
        fclose(gitFile);
        return PATH_STRING();
    }
    fclose(gitFile);

    char* gitdirPath = gitdirLine;
    if (strncmp(gitdirPath, "gitdir: ", 8) == 0)
        gitdirPath += 8;

    // Trim trailing whitespace
    size_t lineLen = strlen(gitdirPath);
    while (lineLen > 0 && (gitdirPath[lineLen - 1] == '\n' ||
           gitdirPath[lineLen - 1] == '\r' ||
           gitdirPath[lineLen - 1] == ' '))
        gitdirPath[--lineLen] = '\0';

    // Extract worktree name — last path component
    // e.g., from ".git/worktrees/my-worktree" extract "my-worktree"
    char* lastSep = strrchr(gitdirPath, '/');
    if (!lastSep)
        lastSep = strrchr(gitdirPath, '\\');

    if (lastSep == NULL)
        return PATH_STRING();

    wchar_t wtName[MAX_PATH];
    MultiByteToWideChar(CP_UTF8, 0, lastSep + 1, -1, wtName, MAX_PATH);
    PATH_STRING suffix = L"_WT_";
    suffix += wtName;
    return suffix;
}

PATH_STRING GetGVFSPipeName(const char *appName)
{
    // The pipe name is built using the path of the GVFS enlistment root.
    // Start in the current directory and walk up the directory tree
    // until we find a folder that contains the ".gvfs" folder.
    // For worktrees, a suffix is appended to target the worktree's mount.

    const size_t dotGVFSRelativePathLength = sizeof(L"\\.gvfs") / sizeof(wchar_t);

    // TODO 640838: Support paths longer than MAX_PATH
    wchar_t enlistmentRoot[MAX_PATH];
    DWORD currentDirResult = GetCurrentDirectoryW(MAX_PATH - dotGVFSRelativePathLength, enlistmentRoot);
    if (currentDirResult == 0 || currentDirResult > MAX_PATH - dotGVFSRelativePathLength)
    {
        die(ReturnCode::GetCurrentDirectoryFailure, "GetCurrentDirectory failed (%d)\n", GetLastError());
    }

    PATH_STRING finalRootPath(GetFinalPathName(enlistmentRoot));
    errno_t copyResult = wcscpy_s(enlistmentRoot, finalRootPath.c_str());
    if (copyResult != 0)
    {
        die(ReturnCode::PipeConnectError, "Could not copy finalRootPath: %ls. Error: %d\n", finalRootPath.c_str(), copyResult);
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
            die(ReturnCode::NotInGVFSEnlistment, "%s must be run from inside a GVFS enlistment\n", appName);
        }

        *(lastslash + 1) = 0;
    };

    *(lastslash) = 0;

    PATH_STRING namedPipe(CharUpperW(enlistmentRoot));
    std::replace(namedPipe.begin(), namedPipe.end(), L':', L'_');
    PATH_STRING pipeName = L"\\\\.\\pipe\\GVFS_" + namedPipe;

    // Append worktree suffix if running in a worktree
    PATH_STRING worktreeSuffix = GetWorktreePipeSuffix(finalRootPath.c_str());
    if (!worktreeSuffix.empty())
    {
        std::transform(worktreeSuffix.begin(), worktreeSuffix.end(),
                       worktreeSuffix.begin(), ::towupper);
        pipeName += worktreeSuffix;
    }

    return pipeName;
}

PIPE_HANDLE CreatePipeToGVFS(const PATH_STRING& pipeName)
{
    PIPE_HANDLE pipeHandle;
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

void DisableCRLFTranslationOnStdPipes()
{
    // set the mode to binary so we don't get CRLF translation
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
}

bool WriteToPipe(PIPE_HANDLE pipe, const char* message, unsigned long messageLength, /* out */ unsigned long* bytesWritten, /* out */ int* error)
{
    BOOL success = WriteFile(
        pipe,                   // pipe handle 
        message,                // message 
        messageLength,          // message length 
        bytesWritten,           // bytes written 
        NULL);                  // not overlapped
    
    *error = success ? 0 : GetLastError();

    return success != FALSE;
}

bool ReadFromPipe(PIPE_HANDLE pipe, char* buffer, unsigned long bufferLength, /* out */ unsigned long* bytesRead, /* out */ int* error)
{
    *error = 0;
    *bytesRead = 0;
    BOOL success = ReadFile(
        pipe,		    	// pipe handle 
        buffer,			    // buffer to receive reply 
        bufferLength,	    // size of buffer 
        bytesRead,         // number of bytes read 
        NULL);              // not overlapped 

    if (!success)
    {
        *error = GetLastError();
    }

    return success || (*error == ERROR_MORE_DATA);
}