// GitHooksLoader.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include <fstream>
#include <string>

int ExecuteHook(const std::wstring &applicationName, wchar_t *hookName, int argc, WCHAR *argv[]);

int wmain(int argc, WCHAR *argv[])
{
	LARGE_INTEGER tickFrequency = { 0 };
	LARGE_INTEGER startTime = { 0 }, endTime = { 0 };
    bool perfTraceEnabled = false;

    size_t requiredCount = 0;
    if (getenv_s(&requiredCount, NULL, 0, "GITHOOKSLOADER_PERFTRACE") != 0)
    {
        requiredCount = 0;
    }

    if (requiredCount != 0)
    {
        // Only enable tracing if we have access to a high res perf counter.
        if (QueryPerformanceFrequency(&tickFrequency) != 0)
        {
            perfTraceEnabled = true;
        }
    }

    if (argc < 2)
    {
        fwprintf(stderr, L"Usage: %s <git verb> [<other arguments>]\n", argv[0]);
        exit(1);
    }

    wchar_t hookName[_MAX_FNAME];
    errno_t err = _wsplitpath_s(argv[0], NULL, 0, NULL, 0, hookName, _MAX_FNAME, NULL, 0);
    if (err != 0)
    {
        fwprintf(stderr, L"Error splitting the path. Error code %d.\n", err);
        exit(2);
    }
    
    std::wstring executingLoader = std::wstring(argv[0]);
    size_t exePartStart = executingLoader.rfind(L".exe");

    if (exePartStart != std::wstring::npos)
    {
        executingLoader.resize(exePartStart);
    }

    std::wifstream hooksList(executingLoader + L".hooks");
    int numHooksExecuted = 0;
    for (std::wstring hookApplication; std::getline(hooksList, hookApplication); )
    {
        // Skip comments and empty lines.
        if (hookApplication.empty() || hookApplication.at(0) == '#')
        {
            continue;
        }

        numHooksExecuted++;

        if (perfTraceEnabled)
        {
            QueryPerformanceCounter(&startTime);
        }

        int hookExitCode = ExecuteHook(hookApplication, hookName, argc, argv);
        if (0 != hookExitCode)
        {
            return hookExitCode;
        }

        if (perfTraceEnabled)
        {
            double elapsedTime;
            QueryPerformanceCounter(&endTime);
            elapsedTime = (endTime.QuadPart - startTime.QuadPart) * 1000.0 / tickFrequency.QuadPart;
            fwprintf(stdout, L"%s: %s = %.2f milliseconds\n", executingLoader.c_str(), hookApplication.c_str(), elapsedTime);
        }
    }

    if (0 == numHooksExecuted)
    {
        fwprintf(stderr, L"No hooks found to execute\n");
        exit(5);
    }

    return 0;
}

int ExecuteHook(const std::wstring &applicationName, wchar_t *hookName, int argc, WCHAR *argv[])
{
    wchar_t expandedPath[MAX_PATH + 1];
    DWORD length = ExpandEnvironmentStrings(applicationName.c_str(), expandedPath, MAX_PATH);
    if (length == 0 || length > MAX_PATH)
    {
        fwprintf(stderr, L"Unable to expand '%s'", applicationName.c_str());
        exit(6);
    }
    
    std::wstring commandLine = std::wstring(expandedPath) + L" " + hookName;
    for (int x = 1; x < argc; x++)
    {
        commandLine += L" " + std::wstring(argv[x]);
    }
    
    // Start the child process. 
    STARTUPINFO si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
    si.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    si.dwFlags = STARTF_USESTDHANDLES;

    ZeroMemory(&pi, sizeof(pi));

    /* The child process will inherit ErrorMode from this process.
     * SEM_FAILCRITICALERRORS will prevent the .NET runtime from
     * creating a dialog box for critical errors - in particular
     * if antivirus has locked the machine.config file.
     * Disabling the dialog box lets the child process (typically GVFS.Hooks.exe)
     * continue trying to run, and if it still needs machine.config then it
     * can handle the exception at that time (whereas the dialog box would
     * hang the app until clicked, and is not handleable by our code).
     */
    UINT previousErrorMode = SetErrorMode(SEM_FAILCRITICALERRORS);

    if (!CreateProcess(
        NULL,           // Application name
        const_cast<LPWSTR>(commandLine.c_str()),
        NULL,           // Process handle not inheritable
        NULL,           // Thread handle not inheritable
        TRUE,           // Set handle inheritance to TRUE
        CREATE_NO_WINDOW, // Process creation flags
        NULL,           // Use parent's environment block
        NULL,           // Use parent's starting directory 
        &si,            // Pointer to STARTUPINFO structure
        &pi)            // Pointer to PROCESS_INFORMATION structure
        )
    {
        fwprintf(stderr, L"Could not execute '%s'. CreateProcess error (%d).\n", applicationName.c_str(), GetLastError());
        SetErrorMode(previousErrorMode);
        exit(3);
    }
    SetErrorMode(previousErrorMode);

    // Wait until child process exits.
    WaitForSingleObject(pi.hProcess, INFINITE);

    // Get process exit code to pass along
    DWORD exitCode;
    if (!GetExitCodeProcess(pi.hProcess, &exitCode))
    {
        fwprintf(stderr, L"GetExitCodeProcess failed (%d).\n", GetLastError());
        exit(4);
    }

    // Close process and thread handles. 
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    return (int)exitCode;
}