#include "KextLogEventTracer.hpp"
#include "kernel-header-wrappers/vnode.h"
#include "Memory.hpp"
#include "KextLog.hpp"
#include "KauthHandler.hpp"
#include <string.h>
#include <libkern/libkern.h>
#include <sys/proc.h>
#include <sys/kauth.h>

RWLock KextLogTracer::traceFilterLock;
_Atomic(char*) KextLogTracer::pathPrefixFilter;
_Atomic(kauth_action_t) KextLogTracer::traceVnodeActionFilterMask;
_Atomic(bool) KextLogTracer::traceDeniedVnodeEvents;
_Atomic(bool) KextLogTracer::traceProviderMessagingEvents;
_Atomic(bool) KextLogTracer::traceAllFileOpEvents;
_Atomic(bool) KextLogTracer::traceAllVnodeEvents;
_Atomic(bool) KextLogTracer::traceCrawlerEvents;
_Atomic(uint64_t) KextLogTracer::nextTraceIndex;

bool KextLogTracer::ShouldTraceEventsForVnode(vnode_t vnode, char (&pathBuffer)[PATH_MAX])
{
    bool shouldTrace = true;
    if (KextLogTracer::pathPrefixFilter != nullptr)
    {
        int pathLen = sizeof(pathBuffer);
        errno_t error = vn_getpath(vnode, pathBuffer, &pathLen);
        if (error == 0)
        {
            RWLock_AcquireShared(KextLogTracer::traceFilterLock);
            {
                if (KextLogTracer::pathPrefixFilter != nullptr)
                {
                    shouldTrace = strprefix(pathBuffer, KextLogTracer::pathPrefixFilter);
                }
            }
            RWLock_ReleaseShared(KextLogTracer::traceFilterLock);
        }
        else
        {
            snprintf(pathBuffer, sizeof(pathBuffer), "[vn_getpath() failed: %d]", error);
        }
    }
    
    return shouldTrace;
}
    
uint64_t KextLogTracer::GetTraceIndex()
{
    if (!this->hasTraceIndex)
    {
        this->traceIndex = atomic_fetch_add(&KextLogTracer::nextTraceIndex, 1llu);
        this->hasTraceIndex = true;
    }
    return this->traceIndex;
}
    
KextLogTracer::TraceBuffer KextLogTracer::GetBuffer()
{
    if (this->dynamicTraceBuffer != nullptr)
    {
        return TraceBuffer { this->dynamicTraceBuffer, this->dynamicTraceBufferSize, this->traceBufferPosition };
    }
    else
    {
        return TraceBuffer { this->embeddedTraceBuffer, sizeof(this->embeddedTraceBuffer), this->traceBufferPosition };
    }
}

bool KextLogTracer::GrowBuffer(uint32_t minimumSize)
{
    TraceBuffer currentBuffer = this->GetBuffer();
    if (minimumSize <= currentBuffer.size)
    {
        return true;
    }
    
    uint32_t newSize = MAX(minimumSize, currentBuffer.size * 2u);
    char* newBuffer = static_cast<char*>(Memory_Alloc(newSize));
    if (newBuffer == nullptr)
    {
        return false;
    }
    
    memcpy(newBuffer, currentBuffer.buffer, currentBuffer.position);
    newBuffer[currentBuffer.position] = '\0';
    
    if (this->dynamicTraceBuffer != nullptr)
    {
        Memory_Free(this->dynamicTraceBuffer, this->dynamicTraceBufferSize);
    }
    this->dynamicTraceBuffer = newBuffer;
    this->dynamicTraceBufferSize = newSize;
    
    return true;
}

void KextLogTracer::Emit()
{
    if (!this->discarded)
    {
        this->willEmitTrace = true;

        TraceBuffer currentBuffer = this->GetBuffer();
        uint64_t index = this->GetTraceIndex();
        KextLog("Event trace %9llu: %.*s", index, currentBuffer.position, currentBuffer.buffer);
    }
}

void KextLogTracer::Flush()
{
    if (!this->discarded)
    {
        this->Emit();
        TraceBuffer currentBuffer = this->GetBuffer();
        currentBuffer.buffer[0] = '\0';
        this->traceBufferPosition = 0;
    }
};

KextLogTracer::KextLogTracer(kauth_action_t vnodeAction, vnode_t vnode) :
    traceBufferPosition(0),
    dynamicTraceBuffer(nullptr),
    dynamicTraceBufferSize(0),
    discarded(false),
    willEmitTrace(false),
    traceIndex(UINT64_MAX),
    hasTraceIndex(false)
{
    this->embeddedTraceBuffer[0] = '\0';
    
    kauth_action_t mask = atomic_load(&KextLogTracer::traceVnodeActionFilterMask);
    if (0 == (mask & vnodeAction))
    {
        this->discarded = true;
    }
    else
    {
        char vnodePath[PATH_MAX] = "";
        if (!ShouldTraceEventsForVnode(vnode, vnodePath))
        {
            this->discarded = true;
        }
        else
        {
            pid_t pid = proc_selfpid();
            char processName[MAXCOMLEN + 1] = "";
            proc_selfname(processName, sizeof(processName));
            if (!atomic_load(&KextLogTracer::traceCrawlerEvents) && KauthHandler_IsFileSystemCrawler(processName))
            {
                this->discarded = true;
            }
            else if (atomic_load(&KextLogTracer::traceAllVnodeEvents))
            {
                this->willEmitTrace = true;
            }
            
            if (vnode_isdir(vnode))
            {
                this->Printf(
                    "Directory vnode '%s' event by process '%s' (PID = %d) action" KextLog_DirectoryVnodeActionFormat,
                    vnodePath,
                    processName,
                    pid,
                    KextLog_DirectoryVnodeActionArgs(vnodeAction, " "));
            }
            else
            {
                this->Printf(
                    "File vnode '%s' event by process '%s' (PID = %d) action" KextLog_FileVnodeActionFormat,
                    vnodePath,
                    processName,
                    pid,
                    KextLog_FileVnodeActionArgs(vnodeAction, " "));
            }
        }
    }
}
    
void KextLogTracer::SendProviderMessage(MessageType providerMessage)
{
    if (!this->discarded)
    {
        if (!this->willEmitTrace && atomic_load(&KextLogTracer::traceProviderMessagingEvents))
        {
            this->willEmitTrace = true;
        }
        this->Printf("\nMessage to provider: %u (%s)", providerMessage, Message_MessageTypeString(providerMessage));
        
        if (this->willEmitTrace)
        {
            this->Emit();
        }
    }
}

void KextLogTracer::SendProviderMessageResult(bool success)
{
    if (!this->discarded)
    {
        this->Printf(" -> result: %s", success ? "success" : "failed");
    }
}

void KextLogTracer::SetVnodeOpResult(int result)
{
    if (this->discarded)
    {
        return;
    }
    
    if (!this->willEmitTrace && atomic_load(&KextLogTracer::traceDeniedVnodeEvents))
    {
        if (result == KAUTH_RESULT_DENY)
        {
            this->willEmitTrace = true;
        }
        else
        {
            this->discarded = true;
            return;
        }
    }
    
    this->Printf("\n-> %s",
        result == KAUTH_RESULT_DENY ? "KAUTH_RESULT_DENY" :
        result == KAUTH_RESULT_ALLOW ? "KAUTH_RESULT_ALLOW" :
        result == KAUTH_RESULT_DEFER ? "KAUTH_RESULT_DEFER" :
        "UNKNOWN");
}

KextLogTracer::~KextLogTracer()
{
    this->Flush();
    
    if (this->dynamicTraceBuffer != nullptr)
    {
        Memory_Free(this->dynamicTraceBuffer, this->dynamicTraceBufferSize);
        this->dynamicTraceBuffer = nullptr;
    }
}
    
template <typename... ARGS>
    void KextLogTracer::Printf(const char* format, ARGS... args)
{
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wformat-security"
    if (this->discarded)
    {
        return;
    }
    
    TraceBuffer currentBuffer = this->GetBuffer();
    uint32_t bufferSpace = currentBuffer.size - currentBuffer.position;
    int sizeRequired = snprintf(currentBuffer.buffer + currentBuffer.position, bufferSpace, format, args...);
    if (sizeRequired >= bufferSpace)
    {
        if (!this->GrowBuffer(sizeRequired))
        {
            this->traceBufferPosition = currentBuffer.size - 1;
            return;
        }
        
        currentBuffer = this->GetBuffer();
        bufferSpace = currentBuffer.size - currentBuffer.position;
        sizeRequired = snprintf(currentBuffer.buffer + currentBuffer.position, bufferSpace, format, args...);
        assert(sizeRequired < bufferSpace);
    }
    
    this->traceBufferPosition += sizeRequired;
#pragma clang diagnostic pop
}

void KextLogTracer::Initialize()
{
    KextLogTracer::traceFilterLock = RWLock_Alloc();
}

void KextLogTracer::Cleanup()
{
    RWLock_FreeMemory(&KextLogTracer::traceFilterLock);
}

