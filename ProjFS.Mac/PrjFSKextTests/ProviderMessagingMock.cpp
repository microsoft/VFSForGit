#include "../PrjFSKext/ProviderMessaging.hpp"
#include "../PrjFSKext/Locks.hpp"
#include "KextLogMock.h"
#include "KextMockUtilities.hpp"

#include <sys/proc.h>
#include <libkern/OSAtomic.h>
#include <sys/kauth.h>


bool ProviderMessaging_Init()
{
    return true;
}


void ProviderMessaging_Cleanup()
{
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
        pid,
        procname,
        kauthResult,
        kauthError);
    return true;
}

void ProviderMessaging_AbortAllOutstandingEvents()
{
}
