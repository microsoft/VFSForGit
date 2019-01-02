#include <string.h>
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

KEXT_STATIC void FindVnodeRootFromDiskAndUpdateCache(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vfs_context_t _Nonnull context,
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    UpdateCacheBehavior updateEntryBehavior,
    /* out parameters */
    VirtualizationRootHandle& rootHandle);

KEXT_STATIC void InsertEntryToInvalidatedCache_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    VirtualizationRootHandle rootHandle);

KEXT_STATIC bool TryFindVnodeIndex_Locked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    /* out parameters */
    uintptr_t& vnodeIndex);

KEXT_STATIC bool TryInsertOrUpdateEntry_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    bool forceRefreshEntry,
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
    FindVnodeRootFromDiskAndUpdateCache(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        context,
        vnode,
        vnodeHashIndex,
        vnodeVid,
        UpdateCacheBehavior_TrustCurrentEntry,
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
    FindVnodeRootFromDiskAndUpdateCache(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        context,
        vnode,
        vnodeHashIndex,
        vnodeVid,
        UpdateCacheBehavior_ForceRefresh,
        rootHandle);
    
    return rootHandle;
}

VirtualizationRootHandle VnodeCache_InvalidateVnodeRootAndGetLatestRoot(
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
    FindVnodeRootFromDiskAndUpdateCache(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        context,
        vnode,
        vnodeHashIndex,
        vnodeVid,
        UpdateCacheBehavior_InvalidateEntry,
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
    uint32_t idealCacheCapacity = expectedVnodeCount * 2;
    
    // Start with a cache capacity of MinPow2VnodeCacheCapacity, and keep increasing
    // it by powers of 2 until the capacity is larger than idealCacheCapacity *or* we've
    // hit the maximum allowed cache capcity
    uint32_t cacheCapacity = MinPow2VnodeCacheCapacity;
    while ((cacheCapacity < idealCacheCapacity) &&
           (cacheCapacity < MaxPow2VnodeCacheCapacity))
    {
        // Increase cacheCapacity by a power of 2
        cacheCapacity = cacheCapacity << 1;
    }
   
    return cacheCapacity;
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
            if (vnode == s_entries[vnodeIndex].vnode &&
                vnodeVid == s_entries[vnodeIndex].vid &&
                RootHandle_Indeterminate != s_entries[vnodeIndex].virtualizationRoot)
            {
                rootFound = true;
                rootHandle = s_entries[vnodeIndex].virtualizationRoot;
            }
        }
    }
    RWLock_ReleaseShared(s_entriesLock);
    
    return rootFound;
}

KEXT_STATIC void FindVnodeRootFromDiskAndUpdateCache(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vfs_context_t _Nonnull context,
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    UpdateCacheBehavior updateEntryBehavior,
    /* out parameters */
    VirtualizationRootHandle& rootHandle)
{
    rootHandle = VirtualizationRoot_FindForVnode(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        vnode,
        context);

    bool forceRefreshEntry;
    VirtualizationRootHandle rootToInsert;
    switch (updateEntryBehavior)
    {
        case UpdateCacheBehavior_ForceRefresh:
            rootToInsert = rootHandle;
            forceRefreshEntry = true;
            break;
        
        case UpdateCacheBehavior_InvalidateEntry:
            rootToInsert = RootHandle_Indeterminate;
            forceRefreshEntry = true;
            break;

        case UpdateCacheBehavior_TrustCurrentEntry:
            rootToInsert = rootHandle;
            forceRefreshEntry = false;
            break;
    
        default:
            assertf(
                false,
                "FindVnodeRootFromDiskAndUpdateCache: Invalid updateEntryBehavior %d",
                updateEntryBehavior);
            
            rootToInsert = rootHandle;
            forceRefreshEntry = false;
            break;
    }

    RWLock_AcquireExclusive(s_entriesLock);
    {
        if (!TryInsertOrUpdateEntry_ExclusiveLocked(
                vnode,
                vnodeHashIndex,
                vnodeVid,
                forceRefreshEntry,
                rootToInsert))
        {
            // TryInsertOrUpdateEntry_ExclusiveLocked can only fail if the cache is full
            perfTracer->IncrementCount(PrjFSPerfCounter_CacheFullCount, true /*ignoreSampling*/);
            InvalidateCache_ExclusiveLocked();
            InsertEntryToInvalidatedCache_ExclusiveLocked(vnode, vnodeHashIndex, vnodeVid, rootToInsert);

        }
    }
    RWLock_ReleaseExclusive(s_entriesLock);
}

// InsertEntryToInvalidatedCache_ExclusiveLocked is a helper function for inserting a vnode
// into the cache after the cache has been invalidated.  This functionality is in its own function
// (rather than in FindVnodeRootFromDiskAndUpdateCache) so that the kext unit tests can force its call
// to TryInsertOrUpdateEntry_ExclusiveLocked to fail.
KEXT_STATIC void InsertEntryToInvalidatedCache_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHashIndex,
    uint32_t vnodeVid,
    VirtualizationRootHandle rootToInsert)
{
    if(!TryInsertOrUpdateEntry_ExclusiveLocked(
                vnode,
                vnodeHashIndex,
                vnodeVid,
                true, // forceRefreshEntry
                rootToInsert))
    {
        KextLog_Error(
            "InsertEntryToInvalidatedCache_ExclusiveLocked: inserting vnode (%p:%u) failed",
            KextLog_Unslide(vnode),
            vnodeVid);
    }
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
        if (forceRefreshEntry ||
            NULLVP == s_entries[vnodeIndex].vnode ||
            vnodeVid != s_entries[vnodeIndex].vid ||
            RootHandle_Indeterminate == s_entries[vnodeIndex].virtualizationRoot)
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
                    "TryInsertOrUpdateEntry_ExclusiveLocked: vnode (%p:%u) has different root in cache(%hd) than was found walking tree(%hd)",
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
