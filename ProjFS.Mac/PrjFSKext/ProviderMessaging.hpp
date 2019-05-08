#pragma once

#include "VirtualizationRoots.hpp"
#include "public/Message.h"

bool ProviderMessaging_Init();
void ProviderMessaging_AbortAllOutstandingEvents();
void ProviderMessaging_Cleanup();

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
    int* kauthError);

void ProviderMessaging_HandleKernelMessageResponse(VirtualizationRootHandle providerVirtualizationRootHandle, uint64_t messageId, MessageType responseType);
void ProviderMessaging_AbortOutstandingEventsForProvider(VirtualizationRootHandle providerVirtualizationRootHandle);
