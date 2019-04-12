#include <string.h>
#include <IOKit/IOUserClient.h>
#include "Locks.hpp"
#include "VnodeCache.hpp"
#include "Memory.hpp"
#include "KextLog.hpp"
#include "../PrjFSKext/public/PrjFSCommon.h"
#include "../PrjFSKext/public/PrjFSHealthData.h"

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

KEXT_STATIC_INLINE void InitCacheStats();
KEXT_STATIC_INLINE void AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat healthStat, uint64_t value);

KEXT_STATIC uint32_t s_entriesCapacity;
KEXT_STATIC VnodeCacheEntry* s_entries;

// s_entriesCapacity will always be a power of 2, and so we can compute the modulo
// using (value & s_ModBitmask) rather than (value % s_entriesCapacity);
KEXT_STATIC uintptr_t s_ModBitmask;

KEXT_STATIC VnodeCacheStats s_cacheStats;

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
    
    InitCacheStats();
    
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
        AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalFindRootForVnodeHits, 1ULL);
        return rootHandle;
    }
    
    perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalFindRootForVnodeMisses, 1ULL);
    
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
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalRefreshRootForVnode, 1ULL);
    
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
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalInvalidateVnodeRoot, 1ULL);
    
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
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_InvalidateEntireCacheCount, 1ULL);
    atomic_exchange(&s_cacheStats.cacheEntries, 0U);

    RWLock_AcquireExclusive(s_entriesLock);
    {
        InvalidateCache_ExclusiveLocked();
    }
    RWLock_ReleaseExclusive(s_entriesLock);
}

IOReturn VnodeCache_ExportHealthData(IOExternalMethodArguments* _Nonnull arguments)
{
    PrjFSHealthData healthData =
    {
        .cacheCapacity = s_entriesCapacity,
        .cacheEntries = s_cacheStats.cacheEntries, // cacheEntries is reset to 0 when VnodeCache_InvalidateCache is called
        .invalidateEntireCacheCount = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount], 0ULL),
        .totalCacheLookups = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_TotalCacheLookups], 0ULL),
        .totalLookupCollisions = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions], 0ULL),
        .totalFindRootForVnodeHits = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount], 0ULL),
        .totalFindRootForVnodeMisses = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses], 0ULL),
        .totalRefreshRootForVnode = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_TotalRefreshRootForVnode], 0ULL),
        .totalInvalidateVnodeRoot = atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_TotalInvalidateVnodeRoot], 0ULL),
    };

    // The buffer will come in either as a memory descriptor or direct pointer, depending on size
    if (nullptr != arguments->structureOutputDescriptor)
    {
        IOMemoryDescriptor* structureOutput = arguments->structureOutputDescriptor;
        if (sizeof(healthData) != structureOutput->getLength())
        {
            KextLog(
                "VnodeCache_ExportHealthData: structure output descriptor size %llu, expected %lu\n",
                static_cast<unsigned long long>(structureOutput->getLength()),
                sizeof(healthData));
            return kIOReturnBadArgument;
        }

        IOReturn result = structureOutput->prepare(kIODirectionIn);
        if (kIOReturnSuccess == result)
        {
            structureOutput->writeBytes(0 /* offset */, &healthData, sizeof(healthData));
            structureOutput->complete(kIODirectionIn);
        }
        
        return result;
    }

    if (arguments->structureOutput == nullptr || arguments->structureOutputSize != sizeof(PrjFSHealthData))
    {
        KextLog("VnodeCache_ExportHealthData: structure output size %u, expected %lu\n", arguments->structureOutputSize, sizeof(healthData));
        return kIOReturnBadArgument;
    }

    memcpy(arguments->structureOutput, &healthData, sizeof(healthData));
    return kIOReturnSuccess;
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
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalCacheLookups, 1ULL);

    // Walk from the starting index until we do one of the following:
    //    -> Find the vnode
    //    -> Find where the vnode should be inserted (i.e. NULLVP)
    //    -> Have looped all the way back to where we started
    uint64_t totalSteps = 0;
    vnodeIndex = vnodeHashIndex;
    while (vnode != s_entries[vnodeIndex].vnode && NULLVP != s_entries[vnodeIndex].vnode)
    {
        ++totalSteps;
        ++vnodeIndex;
        if (vnodeIndex == s_entriesCapacity)
        {
            vnodeIndex = 0;
        }
        
        if (vnodeIndex == vnodeHashIndex)
        {
            // Looped through the entire cache and didn't find an empty slot or the vnode
            AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalLookupCollisions, totalSteps);
            return false;
        }
    }
    
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_TotalLookupCollisions, totalSteps);
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
            if (NULLVP == s_entries[vnodeIndex].vnode)
            {
                atomic_fetch_add(&s_cacheStats.cacheEntries, 1U);
            }
        
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

KEXT_STATIC_INLINE void InitCacheStats()
{
    atomic_exchange(&s_cacheStats.cacheEntries, 0U);
    
    for (int32_t i = 0; i < VnodeCacheHealthStat_Count; ++i)
    {
        atomic_exchange(&s_cacheStats.healthStats[i], 0ULL);
    }
}

KEXT_STATIC_INLINE void AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat healthStat, uint64_t value)
{
    uint64_t statValue = atomic_fetch_add(&s_cacheStats.healthStats[healthStat], value);
    if (statValue > (UINT64_MAX - 1000))
    {
        // The logging daemon is not fetching stats quickly enough (or not running at all)
        // to avoid overflow set this stat back to zero
        KextLog(
                "AtomicFetchAddCacheHealthStat: health stat %d (%s) nearing UINT64_MAX(%llu), resetting to zero",
                healthStat,
                VnodeCacheHealthStatNames[healthStat],
                statValue);
        
        atomic_exchange(&s_cacheStats.healthStats[healthStat], 0ULL);
    }
}
