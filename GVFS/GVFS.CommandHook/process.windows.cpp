#include "stdafx.h"
#include "common.h"
#include "Process.h"

using std::string;

bool Process_IsElevated()
{
    // https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-checktokenmembership
    BOOL b;
    SID_IDENTIFIER_AUTHORITY NtAuthority = SECURITY_NT_AUTHORITY;
    PSID AdministratorsGroup;
    b = AllocateAndInitializeSid(
        &NtAuthority,
        2,
        SECURITY_BUILTIN_DOMAIN_RID,
        DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0,
        &AdministratorsGroup);
    if (b)
    {
        if (!CheckTokenMembership(NULL, AdministratorsGroup, &b))
        {
            b = FALSE;
        }

        FreeSid(AdministratorsGroup);
    }

    return(b);
}

string Process_Run(const string& processName, const string& args, bool redirectOutput)
{
    // https://docs.microsoft.com/en-us/windows/win32/procthread/creating-a-child-process-with-redirected-input-and-output
    HANDLE g_hChildStd_OUT_Rd = NULL;
    HANDLE g_hChildStd_OUT_Wr = NULL;

    if (redirectOutput)
    {
        // Set the bInheritHandle flag so pipe handles are inherited.
        SECURITY_ATTRIBUTES saAttr;
        saAttr.nLength = sizeof(SECURITY_ATTRIBUTES);
        saAttr.bInheritHandle = TRUE;
        saAttr.lpSecurityDescriptor = NULL;

        // Create a pipe for the child process's STDOUT.
        if (!CreatePipe(&g_hChildStd_OUT_Rd, &g_hChildStd_OUT_Wr, &saAttr, 0))
        {
            die(ReturnCode::PipeConnectError, "StdoutRd CreatePipe");
        }

        // Ensure the read handle to the pipe for STDOUT is not inherited.
        if (!SetHandleInformation(g_hChildStd_OUT_Rd, HANDLE_FLAG_INHERIT, 0))
        {
            die(ReturnCode::PipeConnectError, "Stdout SetHandleInformation");
        }
    }

    PROCESS_INFORMATION piProcInfo;
    STARTUPINFOW siStartInfo;
    BOOL bSuccess = FALSE;

    // Set up members of the PROCESS_INFORMATION structure. 
    ZeroMemory(&piProcInfo, sizeof(PROCESS_INFORMATION));

    // Set up members of the STARTUPINFO structure. 
    // This structure specifies the STDIN and STDOUT handles for redirection.
    ZeroMemory(&siStartInfo, sizeof(STARTUPINFO));
    siStartInfo.cb = sizeof(STARTUPINFO);

    if (redirectOutput)
    {
        siStartInfo.hStdError = INVALID_HANDLE_VALUE;
        siStartInfo.hStdOutput = g_hChildStd_OUT_Wr;
        siStartInfo.hStdInput = INVALID_HANDLE_VALUE;
        siStartInfo.dwFlags |= STARTF_USESTDHANDLES;
    }

    std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> utf16conv;
    std::wstring command = utf16conv.from_bytes(processName) + L" " + utf16conv.from_bytes(args);
    wchar_t buffer[4096];

    // TODO (hack): Handle buffer too small
    wcscpy_s(buffer, command.c_str());

    // Create the child process. 
    bSuccess = CreateProcessW(
        NULL,
        buffer,     // command line 
        NULL,          // process security attributes 
        NULL,          // primary thread security attributes 
        TRUE,          // handles are inherited 
        0,             // creation flags 
        NULL,          // use parent's environment 
        NULL,          // use parent's current directory 
        &siStartInfo,  // STARTUPINFO pointer 
        &piProcInfo);  // receives PROCESS_INFORMATION

    // If an error occurs, exit the application. 
    if (!bSuccess)
    {
        die(ReturnCode::LastError, "CreateProcess");
    }

    string output;
    if (redirectOutput)
    {
        CloseHandle(g_hChildStd_OUT_Wr);

        DWORD dwRead;
        CHAR chBuf[4096] = {};
        bSuccess = FALSE;
        for (;;)
        {
            bSuccess = ReadFile(g_hChildStd_OUT_Rd, chBuf, 4096, &dwRead, NULL);
            if (!bSuccess || dwRead == 0)
            {
                break;
            }
            else
            {
                output += chBuf;
            }
        }
    }

    // Wait until child process exits
    WaitForSingleObject(piProcInfo.hProcess, INFINITE);

    CloseHandle(piProcInfo.hProcess);
    CloseHandle(piProcInfo.hThread);

    return output;
}

bool Process_IsConsoleOutputRedirectedToFile()
{
    // Windows specific
    return FILE_TYPE_DISK == GetFileType(GetStdHandle(STD_OUTPUT_HANDLE));
}

bool Process_IsProcessActive(int pid)
{
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
    if (process != NULL)
    {
        DWORD exitCode;
        if (GetExitCodeProcess(process, &exitCode) && exitCode == STILL_ACTIVE)
        {
            CloseHandle(process);
            return true;
        }

        CloseHandle(process);
    }

    return false;
}