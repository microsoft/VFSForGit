#pragma once
#include "stdafx.h"
#include <fcntl.h>
#include <io.h>
#include <algorithm>
#include <string>
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
    CloseHandle(fileHandle);
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

// Reads the first line of a UTF-8 text file into a std::string.
// Returns false if the file cannot be opened or read.
static bool ReadFirstLine(const PATH_STRING& filePath, std::string& line)
{
    FILE* file = NULL;
    errno_t err = _wfopen_s(&file, filePath.c_str(), L"r");
    if (err != 0 || file == NULL)
        return false;

    char buffer[4096];
    if (fgets(buffer, sizeof(buffer), file) == NULL)
    {
        fclose(file);
        return false;
    }
    fclose(file);

    line = buffer;

    // Trim trailing whitespace / newlines
    while (!line.empty() && (line.back() == '\n' || line.back() == '\r' || line.back() == ' '))
        line.pop_back();

    return true;
}

// Converts a UTF-8 string to a wide string.
static PATH_STRING Utf8ToWide(const std::string& utf8)
{
    if (utf8.empty())
        return PATH_STRING();

    int wideLen = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, NULL, 0);
    if (wideLen <= 0)
        return PATH_STRING();

    PATH_STRING wide(wideLen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, &wide[0], wideLen);
    wide.resize(wideLen - 1);
    return wide;
}

// Checks if a directory exists at the given path.
static bool DirectoryExists(const PATH_STRING& path)
{
    DWORD attrs = GetFileAttributesW(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY);
}

// Resolves a potentially relative path against a base directory.
static PATH_STRING ResolvePath(const PATH_STRING& basePath, const PATH_STRING& relativePath)
{
    PATH_STRING combined;
    if (relativePath.length() >= 2 && relativePath[1] == L':')
    {
        combined = relativePath;
    }
    else
    {
        combined = basePath;
        if (!combined.empty() && combined.back() != L'\\')
            combined += L'\\';
        combined += relativePath;
    }

    wchar_t resolved[MAX_PATH];
    DWORD len = GetFullPathNameW(combined.c_str(), MAX_PATH, resolved, NULL);
    if (len == 0 || len >= MAX_PATH)
        return combined;

    return PATH_STRING(resolved);
}

// Parses a .git file to extract the resolved gitdir path and
// worktree name (last component of gitdir path).
static bool TryParseGitFile(
    const PATH_STRING& dotGitFilePath,
    const PATH_STRING& containingDir,
    PATH_STRING& resolvedGitdir,
    std::string& worktreeName)
{
    std::string gitdirLine;
    if (!ReadFirstLine(dotGitFilePath, gitdirLine))
        return false;

    const char* prefix = "gitdir: ";
    if (gitdirLine.compare(0, 8, prefix) != 0)
        return false;

    std::string gitdirPath = gitdirLine.substr(8);
    if (gitdirPath.empty())
        return false;

    std::replace(gitdirPath.begin(), gitdirPath.end(), '/', '\\');

    size_t lastSep = gitdirPath.find_last_of('\\');
    if (lastSep == std::string::npos || lastSep == gitdirPath.length() - 1)
        return false;

    worktreeName = gitdirPath.substr(lastSep + 1);

    PATH_STRING wideGitdir = Utf8ToWide(gitdirPath);
    resolvedGitdir = ResolvePath(containingDir, wideGitdir);
    return true;
}

// Checks if the given directory is a git worktree by looking for a
// ".git" file (not directory). If found, reads it to extract the
// worktree name and returns a pipe name suffix like "_WT_NAME".
// Returns an empty string if not in a worktree.
PATH_STRING GetWorktreePipeSuffix(const wchar_t* directory)
{
    PATH_STRING dotGitPath(directory);
    if (!dotGitPath.empty() && dotGitPath.back() != L'\\')
        dotGitPath += L'\\';
    dotGitPath += L".git";

    DWORD dotGitAttrs = GetFileAttributesW(dotGitPath.c_str());
    if (dotGitAttrs == INVALID_FILE_ATTRIBUTES ||
        (dotGitAttrs & FILE_ATTRIBUTE_DIRECTORY))
    {
        return PATH_STRING();
    }

    PATH_STRING resolvedGitdir;
    std::string worktreeName;
    if (!TryParseGitFile(dotGitPath, PATH_STRING(directory), resolvedGitdir, worktreeName))
        return PATH_STRING();

    // Verify this is actually a worktree (has commondir file)
    PATH_STRING commondirFile = resolvedGitdir + L"\\commondir";
    std::string commondirContent;
    if (!ReadFirstLine(commondirFile, commondirContent))
        return PATH_STRING();

    PATH_STRING suffix = L"_WT_" + Utf8ToWide(worktreeName);
    return suffix;
}

// Walks up from startDirectory looking for a ".git" file (not directory)
// indicating a git worktree. If found, resolves the primary GVFS
// enlistment root through the worktree's gitdir chain:
// 1. Read gvfs-enlistment-root marker (preferred)
// 2. Fall back to commondir -> shared .git dir -> parent -> parent
// Validates that the resolved root contains a .gvfs directory.
static bool TryResolveFromWorktree(
    const PATH_STRING& startDirectory,
    PATH_STRING& enlistmentRoot,
    PATH_STRING& pipeSuffix)
{
    PATH_STRING current = startDirectory;
    while (true)
    {
        PATH_STRING dotGitPath = current + L"\\.git";
        DWORD attrs = GetFileAttributesW(dotGitPath.c_str());

        if (attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY))
        {
            PATH_STRING resolvedGitdir;
            std::string worktreeName;
            if (!TryParseGitFile(dotGitPath, current, resolvedGitdir, worktreeName))
                return false;

            PATH_STRING commondirFile = resolvedGitdir + L"\\commondir";
            std::string commondirContent;
            if (!ReadFirstLine(commondirFile, commondirContent))
                return false;

            // Try gvfs-enlistment-root marker first (written during
            // git worktree add by the managed hooks)
            PATH_STRING markerFile = resolvedGitdir + L"\\gvfs-enlistment-root";
            std::string markerContent;
            if (ReadFirstLine(markerFile, markerContent) && !markerContent.empty())
            {
                std::replace(markerContent.begin(), markerContent.end(), '/', '\\');
                enlistmentRoot = ResolvePath(resolvedGitdir, Utf8ToWide(markerContent));
            }
            else
            {
                // Fall back: commondir -> shared .git dir -> src/ -> enlistment root
                std::replace(commondirContent.begin(), commondirContent.end(), '/', '\\');
                PATH_STRING sharedGitDir = ResolvePath(resolvedGitdir, Utf8ToWide(commondirContent));

                // SharedGitDir = <enlistmentRoot>/src/.git
                size_t sep = sharedGitDir.find_last_of(L'\\');
                if (sep == std::wstring::npos)
                    return false;
                PATH_STRING srcDir = sharedGitDir.substr(0, sep);

                sep = srcDir.find_last_of(L'\\');
                if (sep == std::wstring::npos)
                    return false;
                enlistmentRoot = srcDir.substr(0, sep);
            }

            // Validate: the resolved root must contain .gvfs
            if (!DirectoryExists(enlistmentRoot + L"\\.gvfs"))
                return false;

            pipeSuffix = L"_WT_" + Utf8ToWide(worktreeName);
            return true;
        }

        if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY))
        {
            // Found a .git directory - primary repo, not a worktree
            return false;
        }

        size_t sep = current.find_last_of(L'\\');
        if (sep == std::wstring::npos || sep == 0)
            return false;

        PATH_STRING parent = current.substr(0, sep);
        if (parent == current)
            return false;

        current = parent;
    }
}

PATH_STRING GetGVFSPipeName(const char *appName)
{
    // The pipe name is built using the path of the GVFS enlistment root.
    // Start in the current directory and walk up the directory tree
    // until we find a folder that contains the ".gvfs" folder.
    // For worktrees, a suffix is appended to target the worktree's mount.
    //
    // If .gvfs walk-up fails, fall back to worktree detection: walk up
    // looking for a .git file, then resolve the primary enlistment root
    // through the worktree's gitdir chain.

    const size_t dotGVFSRelativePathLength = sizeof(L"\\.gvfs") / sizeof(wchar_t);

    // TODO 640838: Support paths longer than MAX_PATH
    wchar_t currentDir[MAX_PATH];
    DWORD currentDirResult = GetCurrentDirectoryW(MAX_PATH - dotGVFSRelativePathLength, currentDir);
    if (currentDirResult == 0 || currentDirResult > MAX_PATH - dotGVFSRelativePathLength)
    {
        die(ReturnCode::GetCurrentDirectoryFailure, "GetCurrentDirectory failed (%d)\n", GetLastError());
    }

    PATH_STRING finalRootPath(GetFinalPathName(currentDir));

    // Phase 1: Try .gvfs walk-up (the common case for primary enlistments
    // and worktrees placed under the enlistment root)
    wchar_t enlistmentRoot[MAX_PATH];
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

    bool foundGvfs = false;
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
                foundGvfs = true;
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
            break;
        }

        *(lastslash + 1) = 0;
    }

    if (foundGvfs)
    {
        *(lastslash) = 0;

        PATH_STRING namedPipe(CharUpperW(enlistmentRoot));
        std::replace(namedPipe.begin(), namedPipe.end(), L':', L'_');
        PATH_STRING pipeName = L"\\\\.\\pipe\\GVFS_" + namedPipe;

        PATH_STRING worktreeSuffix = GetWorktreePipeSuffix(finalRootPath.c_str());
        if (!worktreeSuffix.empty())
        {
            std::transform(worktreeSuffix.begin(), worktreeSuffix.end(),
                           worktreeSuffix.begin(), ::towupper);
            pipeName += worktreeSuffix;
        }

        return pipeName;
    }

    // Phase 2: .gvfs not found - try worktree fallback
    PATH_STRING resolvedRoot;
    PATH_STRING worktreeSuffix;
    if (TryResolveFromWorktree(finalRootPath, resolvedRoot, worktreeSuffix))
    {
        std::transform(resolvedRoot.begin(), resolvedRoot.end(),
                       resolvedRoot.begin(), ::towupper);
        std::replace(resolvedRoot.begin(), resolvedRoot.end(), L':', L'_');
        PATH_STRING pipeName = L"\\\\.\\pipe\\GVFS_" + resolvedRoot;

        std::transform(worktreeSuffix.begin(), worktreeSuffix.end(),
                       worktreeSuffix.begin(), ::towupper);
        pipeName += worktreeSuffix;

        return pipeName;
    }

    die(ReturnCode::NotInGVFSEnlistment, "%s must be run from inside a GVFS enlistment\n", appName);
    return PATH_STRING();
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