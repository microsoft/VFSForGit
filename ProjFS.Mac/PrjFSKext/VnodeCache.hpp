#pragma once

#include <sys/kernel_types.h>
#include "VirtualizationRoots.hpp"

kern_return_t VnodeCache_Init();

void VnodeCache_Cleanup();

VirtualizationRootHandle VnodeCache_FindRootForVnode(
        PerfTracer* _Nonnull perfTracer,
        PrjFSPerfCounter cacheHitCounter,
        PrjFSPerfCounter cacheMissCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
        vfs_context_t _Nonnull context,
        vnode_t _Nonnull vnode,
        bool invalidateEntry);

void VnodeCache_InvalidateCache();
