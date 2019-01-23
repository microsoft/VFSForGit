#pragma once

#include <sys/kernel_types.h>
#include "VirtualizationRoots.hpp"

kern_return_t VnodeCache_Init();

void VnodeCache_Cleanup();

VirtualizationRootHandle VnodeCache_FindRootForVnode(
        PerfTracer* perfTracer,
        PrjFSPerfCounter cacheHitCounter,
        PrjFSPerfCounter cacheMissCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
        vfs_context_t context,
        vnode_t vnode,
        bool invalidateEntry);

void VnodeCache_InvalidateCache();
