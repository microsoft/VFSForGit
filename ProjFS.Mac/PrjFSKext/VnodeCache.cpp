#include <string.h>
#include "ArrayUtilities.hpp"
#include "Locks.hpp"
#include "VnodeCache.hpp"
#include "Memory.hpp"
#include "KextLog.hpp"
#include "../PrjFSKext/public/PrjFSCommon.h"

#include "VnodeCachePrivate.hpp"

#ifdef KEXT_UNIT_TESTING
#include "VnodeCacheTestable.hpp"
#endif

KEXT_STATIC_INLINE void InvalidateCache_ExclusiveLocked();
KEXT_STATIC_INLINE uintptr_t ComputeVnodeHashIndex(vnode_t _Nonnull vnode);
KEXT_STATIC_INLINE uint32_t ComputePow2CacheCapacity(int expectedVnodeCount);

KEXT_STATIC bool TryGetVnodeRootFromCache(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    /* out parameters */
    VirtualizationRootHandle& rootHandle);

KEXT_STATIC void LookupVnodeRootAndUpdateCache(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vfs_context_t _Nonnull context,
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    bool forceRefreshEntry,
    /* out parameters */
    VirtualizationRootHandle& rootHandle);

KEXT_STATIC bool TryFindVnodeIndex_Locked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    /* out parameters */
    uintptr_t& vnodeIndex);

KEXT_STATIC bool TryInsertOrUpdateEntry_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    bool invalidateEntry,
    VirtualizationRootHandle rootHandle);

KEXT_STATIC uint32_t s_entriesCapacity;
KEXT_STATIC VnodeCacheEntry* s_entries;

// s_entriesCapacity will always be a power of 2, and so we can compute the modulo
// using (value & s_ModBitmask) rather than (value % s_entriesCapacity);
KEXT_STATIC uintptr_t s_ModBitmask;

static RWLock s_entriesLock;

kern_return_t VnodeCache_Init()
{
    if (RWLock_IsValid(s_entriesLock))
    {
        return KERN_FAILURE;
    }
    
    s_entriesLock = RWLock_Alloc();
    if (!RWLock_IsValid(s_entriesLock))
    {
        return KERN_FAILURE;
    }

    s_entriesCapacity = ComputePow2CacheCapacity(desiredvnodes);
    s_ModBitmask = s_entriesCapacity - 1;
    
    s_entries = Memory_AllocArray<VnodeCacheEntry>(s_entriesCapacity);
    if (nullptr == s_entries)
    {
        s_entriesCapacity = 0;
        return KERN_RESOURCE_SHORTAGE;
    }
    
    memset(s_entries, 0, s_entriesCapacity * sizeof(VnodeCacheEntry));
    
    PerfTracing_RecordSample(PrjFSPerfCounter_CacheCapacity, 0, s_entriesCapacity);
    
    return KERN_SUCCESS;
}

kern_return_t VnodeCache_Cleanup()
{
    if (nullptr != s_entries)
    {
        Memory_FreeArray<VnodeCacheEntry>(s_entries, s_entriesCapacity);
        s_entries = nullptr;
        s_entriesCapacity = 0;
    }
    
    if (RWLock_IsValid(s_entriesLock))
    {
        RWLock_FreeMemory(&s_entriesLock);
        return KERN_SUCCESS;
    }
    
    return KERN_FAILURE;
}

VirtualizationRootHandle VnodeCache_FindRootForVnode(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheHitCounter,
    PrjFSPerfCounter cacheMissCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vnode_t _Nonnull vnode,
    vfs_context_t _Nonnull context)
{
    VirtualizationRootHandle rootHandle = RootHandle_None;
    uintptr_t vnodeHashIndex = ComputeVnodeHashIndex(vnode);
    uint32_t vnodeVid = vnode_vid(vnode);
    
    if (TryGetVnodeRootFromCache(vnode, vnodeHashIndex, vnodeVid, rootHandle))
    {
        perfTracer->IncrementCount(cacheHitCounter, true /*ignoreSampling*/);
        return rootHandle;
    }
    
    perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
    LookupVnodeRootAndUpdateCache(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        context,
        vnode,
        vnodeHashIndex,
        vnodeVid,
        false, // forceRefreshEntry
        rootHandle);
    
    return rootHandle;
}

VirtualizationRootHandle VnodeCache_RefreshRootForVnode(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheHitCounter,
    PrjFSPerfCounter cacheMissCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vnode_t _Nonnull vnode,
    vfs_context_t _Nonnull context)
{
    VirtualizationRootHandle rootHandle = RootHandle_None;
    uintptr_t vnodeHashIndex = ComputeVnodeHashIndex(vnode);
    uint32_t vnodeVid = vnode_vid(vnode);
    
    perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
    LookupVnodeRootAndUpdateCache(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        context,
        vnode,
        vnodeHashIndex,
        vnodeVid,
        true, // forceRefreshEntry
        rootHandle);
    
    return rootHandle;
}

void VnodeCache_InvalidateCache(PerfTracer* _Nonnull perfTracer)
{
    perfTracer->IncrementCount(PrjFSPerfCounter_CacheInvalidateCount, true /*ignoreSampling*/);

    RWLock_AcquireExclusive(s_entriesLock);
    {
        InvalidateCache_ExclusiveLocked();
    }
    RWLock_ReleaseExclusive(s_entriesLock);
}

KEXT_STATIC_INLINE void InvalidateCache_ExclusiveLocked()
{
    memset(s_entries, 0, s_entriesCapacity * sizeof(VnodeCacheEntry));
}

KEXT_STATIC_INLINE uint32_t ComputePow2CacheCapacity(int expectedVnodeCount)
{
    uint32_t idealCacheSize = expectedVnodeCount * 2;
    size_t index = 0;
    uint32_t capacity;
    
    do
    {
        capacity = AllowedPow2CacheCapacities[index];
        ++index;
    }
    while (capacity < idealCacheSize && index < Array_Size(AllowedPow2CacheCapacities));
    
    return capacity;
}

KEXT_STATIC_INLINE uintptr_t ComputeVnodeHashIndex(vnode_t _Nonnull vnode)
{
    uintptr_t vnodeAddress = reinterpret_cast<uintptr_t>(vnode);
    return (vnodeAddress >> 3) & s_ModBitmask;
}

KEXT_STATIC bool TryGetVnodeRootFromCache(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    /* out parameters */
    VirtualizationRootHandle& rootHandle)
{
    bool rootFound = false;
    rootHandle = RootHandle_None;

    RWLock_AcquireShared(s_entriesLock);
    {
        uintptr_t vnodeIndex;
        if (TryFindVnodeIndex_Locked(vnode, vnodeHashIndex, /*out*/ vnodeIndex))
        {
            if (vnode == s_entries[vnodeIndex].vnode && vnodeVid == s_entries[vnodeIndex].vid)
            {
                rootFound = true;
                rootHandle = s_entries[vnodeIndex].virtualizationRoot;
            }
        }
    }
    RWLock_ReleaseShared(s_entriesLock);
    
    return rootFound;
}

KEXT_STATIC void LookupVnodeRootAndUpdateCache(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vfs_context_t _Nonnull context,
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    bool forceRefreshEntry,
    /* out parameters */
    VirtualizationRootHandle& rootHandle)
{
    rootHandle = VirtualizationRoot_FindForVnode(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        vnode,
        context);

    RWLock_AcquireExclusive(s_entriesLock);
    {
        if (!TryInsertOrUpdateEntry_ExclusiveLocked(
                vnode,
                vnodeHashIndex,
                vnodeVid,
                forceRefreshEntry,
                rootHandle))
        {
            // TryInsertOrUpdateEntry_ExclusiveLocked can only fail if the cache is full
            
            perfTracer->IncrementCount(PrjFSPerfCounter_CacheFullCount, true /*ignoreSampling*/);
        
            InvalidateCache_ExclusiveLocked();
            if (!TryInsertOrUpdateEntry_ExclusiveLocked(
                        vnode,
                        vnodeHashIndex,
                        vnodeVid,
                        true, // invalidateEntry
                        rootHandle))
            {
                KextLog_FileError(
                    vnode,
                    "LookupVnodeRootAndUpdateCache: failed to insert vnode (%p:%u) after emptying cache",
                    KextLog_Unslide(vnode), vnodeVid);
            }
        }
    }
    RWLock_ReleaseExclusive(s_entriesLock);
}

KEXT_STATIC bool TryFindVnodeIndex_Locked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    /* out parameters */
    uintptr_t& vnodeIndex)
{
    // Walk from the starting index until we do one of the following:
    //    -> Find the vnode
    //    -> Find where the vnode should be inserted (i.e. NULLVP)
    //    -> Have looped all the way back to where we started
    vnodeIndex = vnodeHashIndex;
    while (vnode != s_entries[vnodeIndex].vnode && NULLVP != s_entries[vnodeIndex].vnode)
    {
        ++vnodeIndex;
        if (vnodeIndex == s_entriesCapacity)
        {
            vnodeIndex = 0;
        }
        
        if (vnodeIndex == vnodeHashIndex)
        {
            // Looped through the entire cache and didn't find an empty slot or the vnode
            return false;
        }
    }
    
    return true;
}

KEXT_STATIC bool TryInsertOrUpdateEntry_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    bool forceRefreshEntry,
    VirtualizationRootHandle rootHandle)
{
    uintptr_t vnodeIndex;
    if (TryFindVnodeIndex_Locked(vnode, vnodeHashIndex, /*out*/ vnodeIndex))
    {
        if (forceRefreshEntry || NULLVP == s_entries[vnodeIndex].vnode || vnodeVid != s_entries[vnodeIndex].vid)
        {
            s_entries[vnodeIndex].vnode = vnode;
            s_entries[vnodeIndex].vid = vnodeVid;
            s_entries[vnodeIndex].virtualizationRoot = rootHandle;
        }
        else
        {
            if (rootHandle != s_entries[vnodeIndex].virtualizationRoot)
            {
                KextLog_FileError(
                    vnode,
                    "TryInsertOrUpdateEntry_ExclusiveLocked: vnode (%p:%u) has different root in cache:%hu than was found walking tree: %hu",
                    KextLog_Unslide(vnode),
                    vnodeVid,
                    s_entries[vnodeIndex].virtualizationRoot,
                    rootHandle);
            }
        }
        
        return true;
    }
    
    return false;
}
