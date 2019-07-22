#pragma once
#include "common.h"

bool GVFSLock_TryAcquireGVFSLockForProcess(
    bool unattended,
    PIPE_HANDLE pipeClient,
    const std::string& fullCommand,
    int pid,
    bool isElevated,
    bool isConsoleOutputRedirectedToFile,
    bool checkAvailabilityOnly,
    const std::string& gitCommandSessionId,
    std::string& result);