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
    int pid,
    const char* procname,
    int* kauthResult,
    int* kauthError,
    void* resultDataBuffer = nullptr,
    size_t resultDataBufferSize = 0,
    size_t* resultDataSize = nullptr);

void ProviderMessaging_HandleKernelMessageResponse(VirtualizationRootHandle providerVirtualizationRootHandle, uint64_t messageId, MessageType responseType, const void* resultData, size_t resultDataSize);
void ProviderMessaging_AbortOutstandingEventsForProvider(VirtualizationRootHandle providerVirtualizationRootHandle);
