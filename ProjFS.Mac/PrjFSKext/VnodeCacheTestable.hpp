#include "public/PrjFSCommon.h"
#include "public/FsidInode.h"
#include "public/PrjFSPerfCounter.h"
#include <sys/kernel_types.h>

#ifndef __cplusplus
#error None of the kext code is set up for being called from C or Objective-C; change the including file to C++ or Objective-C++
#endif

#ifndef KEXT_UNIT_TESTING
#error This class should only be called for unit tests
#endif

// Forward declarations for unit testing
class PerfTracer;
struct VnodeCacheEntry;

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
    uintptr_t vnodeHash,
    /* out parameters */
    uintptr_t& vnodeIndex);

KEXT_STATIC bool TryInsertOrUpdateEntry_ExclusiveLocked(
    vnode_t _Nonnull vnode,
    uintptr_t vnodeHash,
    uint32_t vnodeVid,
    bool forceRefreshEntry,
    VirtualizationRootHandle rootHandle);

KEXT_STATIC_INLINE void InitCacheStats();
KEXT_STATIC_INLINE void AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat healthStat, uint64_t value);

// Static variables used for maintaining Vnode cache state
extern uint32_t s_entriesCapacity;
extern uintptr_t s_ModBitmask;
extern VnodeCacheEntry* _Nullable s_entries;
extern VnodeCacheStats s_cacheStats;

