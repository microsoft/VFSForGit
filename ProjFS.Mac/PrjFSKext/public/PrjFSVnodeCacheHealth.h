#pragma once

struct PrjFSVnodeCacheHealth
{
    // Total capacity of the vnode cache
    uint32_t cacheCapacity;
    
    // Number of entries in the cache (i.e. the number of slots in use)
    uint32_t cacheEntries;
    
    // Number of times that the entire cache has been invalidated
    uint64_t invalidateEntireCacheCount;
    
    // Number of (internal) lookups in the cache array.  This value tracks how many times
    // the VnodeCache functions had to perform a lookup in the cache
    uint64_t totalCacheLookups;
    
    // Number of collisions that occurred when looking up entries in the cache.  Each step
    // of the linear probe counts as a collision.
    uint64_t totalLookupCollisions;
    
    // Number of times that VnodeCache_FindRootForVnode found an up-to-date vnode in the cache
    uint64_t totalFindRootForVnodeHits;
    
    // Number of times that VnodeCache_FindRootForVnode did not find the vnode in the cache (or the
    // vnode in the cache was not up-to-date).
    uint64_t totalFindRootForVnodeMisses;
    
    // Number of times VnodeCache_RefreshRootForVnode was called
    uint64_t totalRefreshRootForVnode;
    
    // Number of times VnodeCache_InvalidateVnodeRootAndGetLatestRoot was called
    uint64_t totalInvalidateVnodeRoot;
};
