#include <string.h>
#include "kernel-header-wrappers/vnode.h"
#include "Locks.hpp"
#include "VnodeUtilities.hpp"
#include "VnodeCache.hpp"
#include "Memory.hpp"
#include "KextLog.hpp"

static inline uintptr_t HashVnode(vnode_t vnode);
static bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, /* out */  uintptr_t& cacheIndex);
static bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, uintptr_t stoppingIndex, /* out */  uintptr_t& cacheIndex);
static void UpdateIndexEntryToLatest_Locked(
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
static uint32_t s_capacity;
static VnodeCacheEntry* s_entries;
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

    s_capacity = desiredvnodes * 2;
    if (s_capacity <= 0)
    {
        return KERN_FAILURE;
    }
    
    s_entries = Memory_AllocArray<VnodeCacheEntry>(s_capacity);
    if (nullptr == s_entries)
    {
        s_capacity = 0;
        RWLock_FreeMemory(&s_entriesLock);
        return KERN_RESOURCE_SHORTAGE;;
    }
    
    VnodeCache_InvalidateCache();
    
    PerfTracing_RecordSample(PrjFSPerfCounter_CacheCapacity, 0, s_capacity);
    
    return KERN_SUCCESS;
}

void VnodeCache_Cleanup()
{
    if (RWLock_IsValid(s_entriesLock))
    {
        RWLock_FreeMemory(&s_entriesLock);
    }

    if (nullptr != s_entries)
    {
        Memory_FreeArray<VnodeCacheEntry>(s_entries, s_capacity);
        s_entries = nullptr;
        s_capacity = 0;
    }
}

// TODO(cache): Add _Nonnull where appropriate
VirtualizationRootHandle VnodeCache_FindRootForVnode(
    PerfTracer* perfTracer,
    PrjFSPerfCounter cacheHitCounter,
    PrjFSPerfCounter cacheMissCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    vfs_context_t context,
    vnode_t vnode,
    bool invalidateEntry)
{
    VirtualizationRootHandle rootHandle = RootHandle_None;
    uintptr_t startingIndex = HashVnode(vnode);
    
    bool lockElevatedToExclusive = false;
    uint32_t vnodeVid = vnode_vid(vnode);
    
    RWLock_AcquireShared(s_entriesLock);
    {
        uintptr_t cacheIndex;
        if (TryFindVnodeIndex_Locked(vnode, startingIndex, /*out*/ cacheIndex))
        {
            if (vnode == s_entries[cacheIndex].vnode)
            {
                // TODO(cache): Also check that the root's vrgid matches what's in the cache
                if (invalidateEntry || vnodeVid != s_entries[cacheIndex].vid)
                {
                    perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
                
                    if (!RWLock_AcquireSharedToExclusive(s_entriesLock))
                    {
                        RWLock_AcquireExclusive(s_entriesLock);
                    }
                    
                    lockElevatedToExclusive = true;
                    UpdateIndexEntryToLatest_Locked(
                        context,
                        perfTracer,
                        cacheMissFallbackFunctionCounter,
                        cacheMissFallbackFunctionInnerLoopCounter,
                        cacheIndex,
                        vnode,
                        vnodeVid);
                }
                else
                {
                    perfTracer->IncrementCount(cacheHitCounter, true /*ignoreSampling*/);
                }
                
                rootHandle = s_entries[cacheIndex].virtualizationRoot;
            }
            else
            {
                perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
            
                // We need to insert the vnode into the cache, upgrade to exclusive lock and add it to the cache
                if (!RWLock_AcquireSharedToExclusive(s_entriesLock))
                {
                    RWLock_AcquireExclusive(s_entriesLock);
                }
                
                lockElevatedToExclusive = true;
                
                // 1. Find the insertion index
                // 2. Look up the virtualization root (if still required)
                
                uintptr_t insertionIndex;
                if (TryFindVnodeIndex_Locked(
                        vnode,
                        cacheIndex,    // starting index
                        startingIndex, // stopping index
                        /*out*/ insertionIndex))
                {
                    if (NULLVP == s_entries[insertionIndex].vnode)
                    {
                        UpdateIndexEntryToLatest_Locked(
                            context,
                            perfTracer,
                            cacheMissFallbackFunctionCounter,
                            cacheMissFallbackFunctionInnerLoopCounter,
                            insertionIndex,
                            vnode,
                            vnodeVid);
                        
                        rootHandle = s_entries[insertionIndex].virtualizationRoot;
                    }
                    else
                    {
                        // We found an existing entry, ensure it's still valid
                        // TODO(cache): Also check that the root's vrgid matches what's in the cache
                        if (invalidateEntry || vnodeVid != s_entries[insertionIndex].vid)
                        {
                            UpdateIndexEntryToLatest_Locked(
                                context,
                                perfTracer,
                                cacheMissFallbackFunctionCounter,
                                cacheMissFallbackFunctionInnerLoopCounter,
                                insertionIndex,
                                vnode,
                                vnodeVid);
                        }
                        
                        rootHandle = s_entries[insertionIndex].virtualizationRoot;
                    }
                }
                else
                {
                    // TODO(cache): We've run out of space in the cache
                    KextLog_FileError(
                        vnode,
                        "vnode cache miss, and no room for additions after re-walk (0x%lu, startingIndex: 0x%lu, cacheIndex: 0x%lu, insertionIndex: 0x%lu)",
                        reinterpret_cast<uintptr_t>(vnode),
                        startingIndex,
                        cacheIndex,
                        insertionIndex);
                }
            }
        }
        else
        {
            // TODO(cache): We've run out of space in the cache
            KextLog_FileError(
                        vnode,
                        "vnode cache miss, and no room for additions (0x%lu)",
                        reinterpret_cast<uintptr_t>(vnode));
        }
    }
    
    if (lockElevatedToExclusive)
    {
        RWLock_ReleaseExclusive(s_entriesLock);
    }
    else
    {
        RWLock_ReleaseShared(s_entriesLock);
    }
    
    return rootHandle;
}

void VnodeCache_InvalidateCache()
{
    RWLock_AcquireExclusive(s_entriesLock);
    
    memset(s_entries, 0, s_capacity * sizeof(VnodeCacheEntry));
    
    RWLock_ReleaseExclusive(s_entriesLock);
}

static inline uintptr_t HashVnode(vnode_t vnode)
{
    uintptr_t vnodeAddress = reinterpret_cast<uintptr_t>(vnode);
    return (vnodeAddress >> 3) % s_capacity;
}

static bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, /* out */  uintptr_t& cacheIndex)
{
    return TryFindVnodeIndex_Locked(vnode, startingIndex, startingIndex, cacheIndex);
}

static bool TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, uintptr_t stoppingIndex, /* out */  uintptr_t& cacheIndex)
{
    // Walk from the starting index until we find:
    //    -> The vnode
    //    -> A NULLVP entry
    //    -> The stopping index
    // If we hit the end of the array, continue searching from the start
    cacheIndex = startingIndex;
    while (vnode != s_entries[cacheIndex].vnode)
    {
        if (NULLVP == s_entries[cacheIndex].vnode)
        {
            return true;
        }
    
        cacheIndex = (cacheIndex + 1) % s_capacity;
        if (cacheIndex == stoppingIndex)
        {
            // Looped through the entire cache and didn't find an empty slot or the vnode
            return false;
        }
    }
    
    return true;
}

static void UpdateIndexEntryToLatest_Locked(
    vfs_context_t context,
    PerfTracer* perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    uintptr_t index,
    vnode_t vnode,
    uint32_t vnodeVid)
{
    FsidInode vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context);
    
    s_entries[index].vnode = vnode;
    s_entries[index].vid = vnodeVid;
    
    // TODO(cache): Add proper perf points
    s_entries[index].virtualizationRoot = VirtualizationRoot_FindForVnode(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        vnode,
        vnodeFsidInode);

    // TODO(cache): Also set the vrgid
}
