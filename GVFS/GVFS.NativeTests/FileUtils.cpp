#include "stdafx.h"
#include "FileUtils.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;

bool SupersedeFile(const wchar_t* path, const char* newContent)
{
    try
    {
        HANDLE handle;
        OBJECT_ATTRIBUTES attributes;
        UNICODE_STRING fullPath;
        IO_STATUS_BLOCK statusBlock;

        std::wstring pathBuilder(L"\\??\\");
        pathBuilder += path;

        RtlInitUnicodeString(&fullPath, pathBuilder.c_str());
        InitializeObjectAttributes(&attributes, &fullPath, 0, NULL, NULL);

        NTSTATUS result = NtCreateFile(
            &handle,
            DELETE | FILE_GENERIC_WRITE | FILE_GENERIC_READ,
            &attributes,
            &statusBlock,
            NULL,
            FILE_ATTRIBUTE_NORMAL,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            FILE_SUPERSEDE,
            FILE_SYNCHRONOUS_IO_NONALERT,
            NULL,
            0);
        SHOULD_EQUAL(result, STATUS_SUCCESS);

        std::string writeContent(newContent);
        result = NtWriteFile(
            handle,
            NULL,
            NULL,
            NULL,
            &statusBlock,
            (PVOID)writeContent.c_str(),
            static_cast<ULONG>(writeContent.length()),
            NULL,
            NULL);

        SHOULD_EQUAL(result, STATUS_SUCCESS);
        SHOULD_EQUAL(statusBlock.Information, writeContent.length());

        NtClose(handle);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}