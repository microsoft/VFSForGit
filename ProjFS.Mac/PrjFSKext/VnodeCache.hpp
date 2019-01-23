#pragma once

#include <sys/kernel_types.h>

#include "VirtualizationRoots.hpp"
#include "Locks.hpp"

class VnodeCache
{
public:
    VnodeCache();
    ~VnodeCache();
    
    bool TryInitialize();
    void Cleanup();
    
    // TODO(cache): Also pass back fsid and inode
    // TODO(cache): Add more perf counters to capture more accurate counts
    VirtualizationRootHandle FindRootForVnode(
        PerfTracer* perfTracer,
        PrjFSPerfCounter cacheHitCounter,
        PrjFSPerfCounter cacheMissCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
        vfs_context_t context,
        vnode_t vnode,
        bool invalidateEntry);
    
    void InvalidateCache();
    
private:
    VnodeCache(const VnodeCache&) = delete;
    VnodeCache& operator=(const VnodeCache&) = delete;
    
    uintptr_t HashVnode(vnode_t vnode);
    bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, /* out */  uintptr_t& cacheIndex);
    bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, uintptr_t stoppingIndex, /* out */  uintptr_t& cacheIndex);
    void UpdateIndexEntryToLatest_Locked(
        vfs_context_t context,
        PerfTracer* perfTracer,
        PrjFSPerfCounter cacheMissFallbackFunctionCounter,
        PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
        uintptr_t index,
        vnode_t vnode,
        uint32_t vnodeVid);
    
    struct VnodeCacheEntry
    {
        vnode_t vnode;
        uint32_t vid;   // vnode generation number
        VirtualizationRootHandle virtualizationRoot;
    };
    
    // Number of VnodeCacheEntry that can be stored in entries
    uint32_t capacity;
    
    VnodeCacheEntry* entries;
    RWLock entriesLock;
};
