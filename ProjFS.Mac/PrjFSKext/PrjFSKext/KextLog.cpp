#include <kern/debug.h>
#include <os/log.h>
#include <stdarg.h>
#include <libkern/libkern.h>
#include <mach/mach_time.h>

#include "KextLog.hpp"
#include "Locks.hpp"
#include "Memory.hpp"
#include "PrjFSLogUserClient.hpp"
#include "PrjFSLogClientShared.h"

os_log_t __prjfs_log;

static PrjFSLogUserClient* s_currentUserClient;
static RWLock s_kextLogRWLock = {};

struct KextLog_StackMessageBuffer
{
    KextLog_MessageHeader header;
    char logString[128];
};

bool KextLog_Init()
{
    // TODO: The subsystem and category values are not currently working. Our events get logged, but are missing these fields.
    __prjfs_log = os_log_create("io.gvfs.PrjFS", "Kext");

    s_kextLogRWLock = RWLock_Alloc();
    if (!RWLock_IsValid(s_kextLogRWLock))
    {
        os_release(__prjfs_log);
        __prjfs_log = nullptr;
        return false;
    }
    return true;
}

void KextLog_Cleanup()
{
    if (RWLock_IsValid(s_kextLogRWLock))
    {
        RWLock_FreeMemory(&s_kextLogRWLock);
    }
    
    if (nullptr != __prjfs_log)
    {
        os_release(__prjfs_log);
        __prjfs_log = nullptr;
    }
}

bool KextLog_RegisterUserClient(PrjFSLogUserClient* userClient)
{
    bool success = false;
    
    RWLock_AcquireExclusive(s_kextLogRWLock);
    {
        if (s_currentUserClient == nullptr)
        {
            s_currentUserClient = userClient;
            success = true;
        }
    }
    RWLock_ReleaseExclusive(s_kextLogRWLock);

    return success;
}

void KextLog_DeregisterUserClient(PrjFSLogUserClient* userClient)
{
    RWLock_AcquireExclusive(s_kextLogRWLock);
    {
        if (userClient == s_currentUserClient)
        {
            s_currentUserClient = nullptr;
        }
    }
    RWLock_ReleaseExclusive(s_kextLogRWLock);
}

void KextLog_Printf(KextLog_Level loglevel, const char* fmt, ...)
{
    // Stack-allocated message with 128-character string buffer for fast path
    struct KextLog_StackMessageBuffer message = {};
    KextLog_MessageHeader* messagePtr = &message.header;

    va_list args;
    va_start(args, fmt);
    int messageLength = vsnprintf(message.logString, sizeof(message.logString), fmt, args);
    va_end (args);
    int messageSize = sizeof(KextLog_MessageHeader) + messageLength + 1 /* null terminator */;

    uint32_t messageFlags = 0;
    bool messageAllocated = false;
    if (messageLength >= sizeof(message.logString))
    {
        messagePtr = static_cast<KextLog_MessageHeader*>(Memory_Alloc(messageSize));
        if (nullptr != messagePtr)
        {
            messageAllocated = true;
            va_start(args, fmt);
            vsnprintf(messagePtr->logString, messageLength + 1, fmt, args);
            va_end (args);
        }
        else
        {
            // Send the truncated in-stack message after all if allocation failed
            messagePtr = &message.header;
            messageSize = sizeof(message);
            messageFlags |= LogMessageFlag_LogMessageTruncated;
        }
    }
    
    RWLock_AcquireShared(s_kextLogRWLock);
    {
        if (s_currentUserClient != nullptr)
        {
            messagePtr->flags = messageFlags;
            messagePtr->level = loglevel;
            uint64_t time = mach_absolute_time();
            messagePtr->machAbsoluteTimestamp = time;
            s_currentUserClient->sendLogMessage(messagePtr, messageSize);
        }
        else
        {
            kprintf("%s\n", messagePtr->logString);
        }
    }
    RWLock_ReleaseShared(s_kextLogRWLock);

    if (messageAllocated)
    {
        Memory_Free(messagePtr, messageSize);
    }
}
