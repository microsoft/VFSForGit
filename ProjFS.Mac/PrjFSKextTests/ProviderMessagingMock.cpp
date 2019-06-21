#include "../PrjFSKext/ProviderMessaging.hpp"
#include "../PrjFSKext/Locks.hpp"
#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/kernel-header-wrappers/kauth.h"
#include "KextLogMock.h"
#include "KextMockUtilities.hpp"

#include <sys/proc.h>
#include <libkern/OSAtomic.h>
#include <sys/kauth.h>
#include "ProviderMessagingMock.hpp"
#include <functional>

using std::function;
using std::move;

bool static s_defaultRequestResult = true;
bool static s_secondRequestResult = true;
static function<void()> s_trySendRequestSideEffect;
int static s_requestCount = 0;

bool ProviderMessaging_Init()
{
    return true;
}


void ProviderMessaging_Cleanup()
{
}

void ProviderMessageMock_SetDefaultRequestResult(bool success)
{
    s_defaultRequestResult = success;
}

void ProviderMessageMock_SetRequestSideEffect(function<void()> sideEffectFunction)
{
    s_trySendRequestSideEffect = move(sideEffectFunction);
}

void ProviderMessageMock_SetSecondRequestResult(bool secondRequestResult)
{
    s_secondRequestResult = secondRequestResult;
}

void ProvidermessageMock_ResetResultCount()
{
    s_requestCount = 0;
    s_trySendRequestSideEffect = nullptr;
}

void ProviderMessaging_HandleKernelMessageResponse(VirtualizationRootHandle providerVirtualizationRootHandle, uint64_t messageId, MessageType responseType)
{
}

void ProviderMessaging_AbortOutstandingEventsForProvider(VirtualizationRootHandle providerVirtualizationRootHandle)
{
}

bool ProviderMessaging_TrySendRequestAndWaitForResponse(
    VirtualizationRootHandle root,
    MessageType messageType,
    const vnode_t vnode,
    const FsidInode& vnodeFsidInode,
    const char* vnodePath,
    const char* fromPath,
    int pid,
    const char* procname,
    int* kauthResult,
    int* kauthError)
{
    MockCalls::RecordFunctionCall(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        root,
        messageType,
        vnode,
        vnodeFsidInode,
        vnodePath,
        fromPath,
        pid,
        procname,
        kauthResult,
        kauthError);
    
    if (s_trySendRequestSideEffect)
    {
        s_trySendRequestSideEffect();
    }
    
    s_requestCount++;
    
    bool result;
    if (s_requestCount == 2)
    {
        result = s_secondRequestResult;
    }
    else
    {
        result = s_defaultRequestResult;
    }
    
    *kauthResult = KAUTH_RESULT_DEFER;
    if (result == false)
    {
        *kauthResult = KAUTH_RESULT_DENY;
    }
    
    return result;
}

void ProviderMessaging_AbortAllOutstandingEvents()
{
}
