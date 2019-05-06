#pragma once

#include "kernel-header-wrappers/stdatomic.h"
#include "public/ArrayUtils.hpp"

enum UpdateCacheBehavior
{
    UpdateCacheBehavior_Invalid = 0,
    
    // If the current entry is up-to-date it will be used
    UpdateCacheBehavior_TrustCurrentEntry,
    
    // The current entry will be replaced with the new root
    UpdateCacheBehavior_ForceRefresh,
    
    // The current entry will have its root marked as invalid (forcing the next lookup to find the root)
    UpdateCacheBehavior_InvalidateEntry,
};

struct VnodeCacheEntry
{
    vnode_t vnode;
    uint32_t vid;   // vnode generation number
    VirtualizationRootHandle virtualizationRoot;
};

enum VnodeCacheHealthStat : int32_t
{
    VnodeCacheHealthStat_InvalidateEntireCacheCount,
    VnodeCacheHealthStat_TotalCacheLookups,
    VnodeCacheHealthStat_TotalLookupCollisions,
    VnodeCacheHealthStat_TotalFindRootForVnodeHits,
    VnodeCacheHealthStat_TotalFindRootForVnodeMisses,
    VnodeCacheHealthStat_TotalRefreshRootForVnode,
    VnodeCacheHealthStat_TotalInvalidateVnodeRoot,
    
    VnodeCacheHealthStat_Count
};

static constexpr const char* const VnodeCacheHealthStatNames[VnodeCacheHealthStat_Count] =
{
    [VnodeCacheHealthStat_InvalidateEntireCacheCount]  = "InvalidateEntireCacheCount",
    [VnodeCacheHealthStat_TotalCacheLookups]           = "TotalCacheLookups",
    [VnodeCacheHealthStat_TotalLookupCollisions]       = "TotalLookupCollisions",
    [VnodeCacheHealthStat_TotalFindRootForVnodeHits]   = "TotalFindRootForVnodeHits",
    [VnodeCacheHealthStat_TotalFindRootForVnodeMisses] = "TotalFindRootForVnodeMisses",
    [VnodeCacheHealthStat_TotalRefreshRootForVnode]    = "TotalRefreshRootForVnode",
    [VnodeCacheHealthStat_TotalInvalidateVnodeRoot]    = "TotalInvalidateVnodeRoot",
};

struct VnodeCacheStats
{
    _Atomic(uint32_t) cacheEntries;
    _Atomic(uint64_t) healthStats[VnodeCacheHealthStat_Count];
};

static_assert(AllArrayElementsInitialized(VnodeCacheHealthStatNames), "There must be an initialization of VnodeCacheHealthStatNames elements corresponding to each VnodeCacheHealthStat enum value");

// Allow cache the cache to use between 4 MB and 64 MB of memory (assuming 16 bytes per VnodeCacheEntry)
KEXT_STATIC const uint32_t MinPow2VnodeCacheCapacity = 0x040000;
KEXT_STATIC const uint32_t MaxPow2VnodeCacheCapacity = 0x400000;
