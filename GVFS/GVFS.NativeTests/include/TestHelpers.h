#pragma once

#include "Should.h"
#include "prjlib_internal.h"

// Map ProjFS testing macros to GVFS testing macros
#define VERIFY_ARE_EQUAL SHOULD_EQUAL
#define VERIFY_ARE_NOT_EQUAL SHOULD_NOT_EQUAL
#define VERIFY_FAIL FAIL_TEST

static const DWORD MAX_BUF_SIZE = 256;

struct FileInfo
{
    std::string Name;
    bool IsFile = true;
    DWORD FileSize = 0;
};

namespace TestHelpers
{

inline std::shared_ptr<void> OpenForRead(const std::string& path)
{
    std::shared_ptr<void> handle(
        CreateFile(path.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL),
        CloseHandle);

    if (INVALID_HANDLE_VALUE == handle.get()) {
        VERIFY_FAIL("failed to open file for read");
    }

    VERIFY_ARE_NOT_EQUAL(INVALID_HANDLE_VALUE, handle.get());
    return handle;
}

inline std::vector<FileInfo> EnumDirectory(const std::string& path)
{
    WIN32_FIND_DATA ffd;

    std::vector<FileInfo> result;

    std::string query = path + "*";

    HANDLE hFind = FindFirstFile(query.c_str(), &ffd);

    if (hFind == INVALID_HANDLE_VALUE)
    {
        VERIFY_FAIL("FindFirstFile failed");
    }

    do
    {
        FileInfo fileInfo;
        fileInfo.Name = ffd.cFileName;

        if (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            fileInfo.IsFile = false;
        }
        else
        {
            fileInfo.FileSize = ffd.nFileSizeLow;
        }

        result.push_back(fileInfo);
    } while (FindNextFile(hFind, &ffd) != 0);

    DWORD dwError = GetLastError();
    if (dwError != ERROR_NO_MORE_FILES)
    {
        VERIFY_FAIL("FindNextFile failed");
    }

    FindClose(hFind);

    return result;
}

inline void WriteToFile(const std::string& path, const std::string content, bool isNewFile = false)
{
    HANDLE hFile = CreateFile(path.c_str(),
        GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        isNewFile ? CREATE_NEW : OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        VERIFY_FAIL("CreateFile failed");
    }

    if (content.empty())
    {
        goto CleanUp;
    }

    DWORD dwBytesToWrite = (DWORD)content.size() * sizeof(content[0]);
    DWORD dwBytesWritten = 0;

    VERIFY_ARE_EQUAL(TRUE, WriteFile(
        hFile,           // open file handle
        content.c_str(), // start of data to write
        dwBytesToWrite,  // number of bytes to write
        &dwBytesWritten, // number of bytes that were written
        NULL));

    VERIFY_ARE_EQUAL(dwBytesToWrite, dwBytesWritten);

    VERIFY_ARE_EQUAL(TRUE, FlushFileBuffers(hFile));

CleanUp:
    CloseHandle(hFile);
}

inline void CreateNewFile(const std::string& path, const std::string content)
{
    WriteToFile(path, content, true);
}

inline void CreateNewFile(const std::string& path)
{
    CreateNewFile(path, "");
}

inline DWORD DelFile(const std::string& path, bool isSetDisposition = true)
{
    if (isSetDisposition) {
        BOOL success = DeleteFile(path.c_str());
        if (success) {
            return ERROR_SUCCESS;
        }
        else {
            return GetLastError();
        }
    }

    // delete on close
    HANDLE handle = CreateFile(
        path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,
        0,
        OPEN_EXISTING,
        FILE_FLAG_DELETE_ON_CLOSE,
        NULL);

    if (handle == INVALID_HANDLE_VALUE) {
        return GetLastError();
    }

    BOOL success = CloseHandle(handle);
    if (!success) {
        return GetLastError();
    }

    return ERROR_SUCCESS;
}

inline std::shared_ptr<GV_REPARSE_INFO> GetReparseInfo(const std::string& path)
{
    USHORT dataSize = MAXIMUM_REPARSE_DATA_BUFFER_SIZE;
    std::shared_ptr<GV_REPARSE_INFO> reparseInfo((PGV_REPARSE_INFO)calloc(1, dataSize), free);

    std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> utf16conv;
    
    ULONG reparseTag;
    HRESULT hr = PrjpReadPrjReparsePointData(utf16conv.from_bytes(path).c_str(), reparseInfo.get(), &reparseTag, &dataSize);
    if (FAILED(hr)) {
        if (hr == HRESULT_FROM_WIN32(ERROR_NOT_A_REPARSE_POINT)) {
            // ERROR: target is not a reparse point
            return false;
        }
        else {
            // ERROR: failed to read reparse point
            return false;
        }
    }

    return reparseInfo;
}

inline bool IsFullFolder(const std::string& path)
{
    unsigned long flag = GetReparseInfo(path)->Flags & GV_FLAG_FULLY_POPULATED;

    return flag != 0;
}

inline bool DoesFileExist(const std::string& path)
{
    HANDLE handle = CreateFile(path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (handle != INVALID_HANDLE_VALUE) {
        CloseHandle(handle);
        return true;
    }

    if (ERROR_FILE_NOT_FOUND == GetLastError()) {
        return false;
    }

    return false;
}

inline std::string ReadFileAsString(const std::string& path)
{
    std::shared_ptr<void> hFile = OpenForRead(path);

    char DataBuffer[MAX_BUF_SIZE] = { 0 };
    DWORD dwbytesRead;

    VERIFY_ARE_NOT_EQUAL(ReadFile(
        hFile.get(),
        DataBuffer,
        MAX_BUF_SIZE,
        &dwbytesRead,
        NULL
    ), FALSE);

    return std::string(DataBuffer);
}

inline DWORD DelFolder(const std::string& path, bool isSetDisposition = true)
{
    if (isSetDisposition) {
        auto success = RemoveDirectory(path.c_str());
        if (success) {
            return ERROR_SUCCESS;
        }
        else {
            return GetLastError();
        }
    }

    // delete on close
    HANDLE handle = CreateFile(
        path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,
        0,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_DELETE_ON_CLOSE,
        NULL);

    if (handle == INVALID_HANDLE_VALUE) {
        return GetLastError();
    }

    BOOL success = CloseHandle(handle);
    if (!success) {
        return GetLastError();
    }

    return ERROR_SUCCESS;
}

inline HRESULT CreateDirectoryWithIntermediates(
    _In_ const std::string& directoryName
)
{
    if (!CreateDirectory(directoryName.c_str(), nullptr)) {

        int gle = GetLastError();
        if (gle == ERROR_ALREADY_EXISTS) {

            //  If the directory already exists just treat that as success.
            return S_OK;

        }
        else if (gle == ERROR_PATH_NOT_FOUND) {

            //  One or more intermediate directories don't exist.  Assume
            //  the incoming path starts with e.g "X:\"
            std::string ntPath = "\\\\?\\";
            size_t startPos = 3;
            if (directoryName.compare(0, ntPath.length(), ntPath) == 0) {
                startPos += ntPath.length();
            }

            std::string::size_type foundPos = directoryName.find_first_of("\\", startPos);
            while (foundPos != std::string::npos) {

                if (!CreateDirectory(directoryName.substr(0, foundPos).c_str(), nullptr)) {

                    gle = GetLastError();
                    if (gle != ERROR_ALREADY_EXISTS) {
                        return HRESULT_FROM_WIN32(gle);
                    }
                }

                foundPos = directoryName.find_first_of("\\", foundPos + 1);
            }

            //  The loop created all the intermediate directories.  Try creating the final
            //  part again unless the string ended in a "\".  In that case we created everything
            //  we need.

            if (directoryName.length() - 1 != directoryName.find_last_of("\\")) {

                if (!CreateDirectory(directoryName.c_str(), nullptr)) {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
            }

        }
        else {
            return HRESULT_FROM_WIN32(gle);
        }
    }

    return S_OK;
}

inline std::shared_ptr<void> OpenForQueryAttribute(const std::string& path)
{
    std::shared_ptr<void> handle(
        CreateFile(path.c_str(),
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL),
        CloseHandle);

    VERIFY_ARE_NOT_EQUAL(INVALID_HANDLE_VALUE, handle.get());

    return handle;
}

inline FILETIME GetLastWriteTime(const std::string& path)
{
    std::shared_ptr<void> hFile = OpenForQueryAttribute(path);

    FILETIME ftWrite;
    VERIFY_ARE_EQUAL(TRUE, GetFileTime(hFile.get(), NULL, NULL, &ftWrite));
    SYSTEMTIME systemTime = { 0 };

    BOOL success = FileTimeToSystemTime(&ftWrite, &systemTime);
    VERIFY_ARE_EQUAL(TRUE, success);    

    return ftWrite;
}

inline LARGE_INTEGER GetFileSize(const std::string& path)
{
    std::shared_ptr<void> hFile = OpenForQueryAttribute(path);

    LARGE_INTEGER size;
    VERIFY_ARE_EQUAL(TRUE, GetFileSizeEx(hFile.get(), &size));
    return size;
}

inline NTSTATUS SetEAInfo(const std::string& path, PFILE_FULL_EA_INFORMATION pbEABuffer, ULONG size, const int attributeNum = 1) {

    HANDLE hFile = CreateFile(path.c_str(),
        FILE_WRITE_EA,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (INVALID_HANDLE_VALUE == hFile)
    {
        printf("\nSetExtendedAttributes: Cannot create handle for file %s", path.c_str());
        VERIFY_FAIL(("SetExtendedAttributes: Cannot create handle for file " + path).c_str());
    }

    NTSTATUS NtStatus = STATUS_SUCCESS;
    IO_STATUS_BLOCK IoStatusBlock;

    PFILE_FULL_EA_INFORMATION pEABlock = NULL;
    CHAR xAttrName[MAX_PATH] = { 0 };
    CHAR xAttrValue[MAX_PATH] = { 0 };

    for (int i = 0; i<attributeNum; i++)
    {
        HRESULT hr = StringCchPrintf(xAttrName, MAX_PATH, "Test Extended Attribute Name %d\0", i);
        if (!SUCCEEDED(hr))
        {
            printf("\n\tSetExtendedAttributes: StringCchPrintfA failed (0x%08x)", hr);
            VERIFY_FAIL("SetExtendedAttributes: StringCchPrintfA failed");
        }

        hr = StringCchPrintf(xAttrValue, MAX_PATH, "Test Extended Attribute Value %d\0", i);
        if (!SUCCEEDED(hr))
        {
            printf("\n\tSetExtendedAttributes: StringCchPrintfA failed (0x%08x)", hr);
            VERIFY_FAIL("SetExtendedAttributes: StringCchPrintfA failed");
        }

        ZeroMemory(pbEABuffer, 2048);
        ZeroMemory(&IoStatusBlock, sizeof(IoStatusBlock));
        pEABlock = (PFILE_FULL_EA_INFORMATION)pbEABuffer;
        pEABlock->NextEntryOffset = 0;
        pEABlock->Flags = 0;
        pEABlock->EaNameLength = (UCHAR)(lstrlenA(xAttrName) * sizeof(CHAR));   // in bytes;
        pEABlock->EaValueLength = (UCHAR)(lstrlenA(xAttrValue) * sizeof(CHAR)); // in bytes;

        CopyMemory(pEABlock->EaName, xAttrName, lstrlenA(xAttrName) * sizeof(CHAR));
        pEABlock->EaName[pEABlock->EaNameLength] = 0; // IO subsystem checks for this NULL

        CopyMemory(pEABlock->EaName + pEABlock->EaNameLength + 1, xAttrValue, pEABlock->EaValueLength + 1);
        pEABlock->EaName[pEABlock->EaNameLength + 1 + pEABlock->EaValueLength + 1] = 0; // IO subsystem checks for this NULL

        HMODULE ntdll = LoadLibrary("ntdll.dll");
        VERIFY_ARE_NOT_EQUAL(ntdll, NULL);

        PSetEaFile NtSetEaFile = (PSetEaFile)GetProcAddress(ntdll, "NtSetEaFile");
        VERIFY_ARE_NOT_EQUAL(NtSetEaFile, NULL);

        NtStatus = NtSetEaFile(hFile, &IoStatusBlock, (PVOID)pEABlock, size);
        if (!NT_SUCCESS(NtStatus))
        {
            printf("\n\tSetExtendedAttributes: Failed in NtSetEaFile (0x%08x)", NtStatus);
            VERIFY_FAIL("SetExtendedAttributes: Failed in NtSetEaFile");
        }
    }

    CloseHandle(hFile);

    return NtStatus;
}

inline NTSTATUS ReadEAInfo(const std::string& path, PFILE_FULL_EA_INFORMATION eaBuffer, PULONG length) {

    NTSTATUS status = STATUS_SUCCESS;

    HANDLE hFile = CreateFile(path.c_str(),
        FILE_READ_EA | SYNCHRONIZE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (INVALID_HANDLE_VALUE == hFile)
    {
        VERIFY_FAIL("ReadEAInfo: Cannot create handle for file");
    }

    IO_STATUS_BLOCK IoStatusBlock;
    
    // In the ProjFS tests, Index of 0 is used, however, per the EA comments
    // "If the index value is zero, there are no Eas to return"  Confirmed index of 1
    // properly reads EAs created using ea.exe test tool provided by ProjFS
    ULONG Index = 1;
    FILE_EA_INFORMATION eaInfo = { 0 };

    HMODULE ntdll = LoadLibrary("ntdll.dll");
    VERIFY_ARE_NOT_EQUAL(ntdll, NULL);

    PQueryInformationFile NtQueryInformationFile = (PQueryInformationFile)GetProcAddress(ntdll, "NtQueryInformationFile");
    VERIFY_ARE_NOT_EQUAL(NtQueryInformationFile, NULL);

    status = NtQueryInformationFile(
        hFile,
        &IoStatusBlock,
        &eaInfo,
        sizeof(eaInfo),
        FileEaInformation
    );

    if (!NT_SUCCESS(status)) {
        printf("\n\tError: NtQueryInformationFile failed, status = 0x%lx\n", status);
        goto Cleanup;
    }

    if (eaInfo.EaSize) {

        if (*length < eaInfo.EaSize) {
            printf("\n\tNtQueryEaFile failed, buffer is too small\n");
            status = ERROR_NOT_ENOUGH_MEMORY;
            goto Cleanup;
        }

        *length = eaInfo.EaSize;

        PQueryEaFile NtQueryEaFile = (PQueryEaFile)GetProcAddress(ntdll, "NtQueryEaFile");
        VERIFY_ARE_NOT_EQUAL(NtQueryEaFile, NULL);

        status = NtQueryEaFile(
            hFile,
            &IoStatusBlock,
            eaBuffer,
            *length,
            FALSE,
            NULL,
            0,
            &Index,
            TRUE);

        if (!NT_SUCCESS(status)) {
            printf("\n\tNtQueryEaFile failed, status = 0x%lx\n", status);
            goto Cleanup;
        }
    }

Cleanup:
    CloseHandle(hFile);

    return status;
}

inline std::string CombinePath(const std::string& root, const std::string& relPath)
{
    std::string fullPath = root;

    if (root.empty() || root == "\\") {
        return relPath;
    }

    if (fullPath.back() == '\\') {
        fullPath.pop_back();
    }

    if (!relPath.empty()) {
        fullPath += '\\';
        fullPath += relPath;
    }

    if (fullPath.back() == '\\') {
        fullPath.pop_back();
    }

    return fullPath;
}

inline std::string ReadFileAsStringUncached(const std::string& path)
{
    HANDLE hFile = CreateFile(
        path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_RANDOM_ACCESS,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE) {
        VERIFY_FAIL("CreateFile failed");
    }

    VERIFY_ARE_NOT_EQUAL(INVALID_HANDLE_VALUE, hFile);
    VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, GetLastError());

    HANDLE hMapFile = CreateFileMapping(
        hFile,
        NULL,                    // default security
        PAGE_READWRITE | FILE_MAP_READ,          // read/write access
        0,                       // maximum object size (high-order DWORD)
        0,                      // maximum object size (low-order DWORD)
        NULL);                 // name of mapping object

    VERIFY_ARE_NOT_EQUAL(INVALID_HANDLE_VALUE, hMapFile);
    VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, GetLastError());

    LPCTSTR pBuf = (LPTSTR)MapViewOfFile(hMapFile,   // handle to map object
        FILE_MAP_READ,               // read permission
        0,
        0,
        0);

    VERIFY_ARE_EQUAL((DWORD)ERROR_SUCCESS, GetLastError());
    VERIFY_ARE_NOT_EQUAL(nullptr, pBuf);

    std::string result(pBuf);

    UnmapViewOfFile(pBuf);

    CloseHandle(hMapFile);

    CloseHandle(hFile);

    return result;
}

inline int MovFile(const std::string& from, const std::string& to)
{
    int ret = rename(from.c_str(), to.c_str());
    if (ret != 0) {
        errno_t err;
        _get_errno(&err);
        return err;
    }

    return ret;
}

inline bool NewHardLink(const std::string& newlink, const std::string& existingFile)
{
    auto created = CreateHardLink(newlink.c_str(), existingFile.c_str(), NULL);
    return created == TRUE;
}

inline void VerifyEnumerationMatches(void* folderHandle, PUNICODE_STRING filter, const std::vector<std::wstring>& expectedContents)
{
    SHOULD_NOT_EQUAL(folderHandle, INVALID_HANDLE_VALUE);

    UCHAR buffer[2048];
    NTSTATUS status;
    IO_STATUS_BLOCK ioStatus;
    BOOLEAN restart = TRUE;
    size_t expectedIndex = 0;

    do
    {
        status = NtQueryDirectoryFile(folderHandle,
            NULL,
            NULL,
            NULL,
            &ioStatus,
            buffer,
            ARRAYSIZE(buffer),
            FileBothDirectoryInformation,
            FALSE,
            filter,
            restart);

        if (status == STATUS_SUCCESS)
        {
            PFILE_BOTH_DIR_INFORMATION dirInfo;
            PUCHAR entry = buffer;

            do
            {
                dirInfo = (PFILE_BOTH_DIR_INFORMATION)entry;

                std::wstring entryName(dirInfo->FileName, dirInfo->FileNameLength / sizeof(WCHAR));

                SHOULD_EQUAL(entryName, expectedContents[expectedIndex]);

                entry = entry + dirInfo->NextEntryOffset;
                ++expectedIndex;

            } while (dirInfo->NextEntryOffset > 0 && expectedIndex < expectedContents.size());

            restart = FALSE;
        }

    } while (status == STATUS_SUCCESS);

    SHOULD_EQUAL(expectedIndex, expectedContents.size());
    SHOULD_EQUAL(status, STATUS_NO_MORE_FILES);
}

inline void VerifyEnumerationMatches(void* folderHandle, const std::vector<std::wstring>& expectedContents)
{
    VerifyEnumerationMatches(folderHandle, nullptr, expectedContents);
}

} // namespace TestHelpers