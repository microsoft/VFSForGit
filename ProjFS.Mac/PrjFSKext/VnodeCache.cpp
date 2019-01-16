#include <string.h>
#include "kernel-header-wrappers/vnode.h"
#include "VnodeUtilities.hpp"
#include "VnodeCache.hpp"
#include "Memory.hpp"
#include "KextLog.hpp"

VnodeCache::VnodeCache()
    : capacity(0)
    , entries(nullptr)
{
}

VnodeCache::~VnodeCache()
{
}

bool VnodeCache::TryInitialize()
{
    if (RWLock_IsValid(this->entriesLock))
    {
        return false;
    }
    
    this->entriesLock = RWLock_Alloc();
    if (!RWLock_IsValid(this->entriesLock))
    {
        return false;
    }

    this->capacity = desiredvnodes * 2;
    if (this->capacity <= 0)
    {
        return false;
    }
    
    this->entries = Memory_AllocArray<VnodeCacheEntry>(this->capacity);
    if (nullptr == this->entries)
    {
        this->capacity = 0;
        return false;
    }
    
    memset(this->entries, 0, this->capacity * sizeof(VnodeCacheEntry));
    
    PerfTracing_RecordSample(PrjFSPerfCounter_CacheCapacity, 0, this->capacity);
    
    return true;
}

void VnodeCache::Cleanup()
{
    if (RWLock_IsValid(this->entriesLock))
    {
        RWLock_FreeMemory(&this->entriesLock);
    }

    if (nullptr != this->entries)
    {
        Memory_FreeArray<VnodeCacheEntry>(this->entries, this->capacity);
        this->entries = nullptr;
        this->capacity = 0;
    }
}

// TODO(cache): Add _Nonnull where appropriate
VirtualizationRootHandle VnodeCache::FindRootForVnode(
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
    uintptr_t startingIndex = this->HashVnode(vnode);
    
    bool lockElevatedToExclusive = false;
    uint32_t vnodeVid = vnode_vid(vnode);
    
    RWLock_AcquireShared(this->entriesLock);
    {
        uintptr_t cacheIndex;
        if (this->TryFindVnodeIndex_Locked(vnode, startingIndex, /*out*/ cacheIndex))
        {
            if (vnode == this->entries[cacheIndex].vnode)
            {
                // TODO(cache): Also check that the root's vrgid matches what's in the cache
                if (invalidateEntry || vnodeVid != this->entries[cacheIndex].vid)
                {
                    perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
                
                    if (!RWLock_AcquireSharedToExclusive(this->entriesLock))
                    {
                        RWLock_AcquireExclusive(this->entriesLock);
                    }
                    
                    lockElevatedToExclusive = true;
                    this->UpdateIndexEntryToLatest_Locked(
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
                
                rootHandle = this->entries[cacheIndex].virtualizationRoot;
            }
            else
            {
                perfTracer->IncrementCount(cacheMissCounter, true /*ignoreSampling*/);
            
                // We need to insert the vnode into the cache, upgrade to exclusive lock and add it to the cache
                if (!RWLock_AcquireSharedToExclusive(this->entriesLock))
                {
                    RWLock_AcquireExclusive(this->entriesLock);
                }
                
                lockElevatedToExclusive = true;
                
                // 1. Find the insertion index
                // 2. Look up the virtualization root (if still required)
                
                uintptr_t insertionIndex;
                if (this->TryFindVnodeIndex_Locked(
                        vnode,
                        cacheIndex,    // starting index
                        startingIndex, // stopping index
                        /*out*/ insertionIndex))
                {
                    if (NULLVP == this->entries[insertionIndex].vnode)
                    {
                        this->UpdateIndexEntryToLatest_Locked(
                            context,
                            perfTracer,
                            cacheMissFallbackFunctionCounter,
                            cacheMissFallbackFunctionInnerLoopCounter,
                            insertionIndex,
                            vnode,
                            vnodeVid);
                        
                        rootHandle = this->entries[insertionIndex].virtualizationRoot;
                    }
                    else
                    {
                        // We found an existing entry, ensure it's still valid
                        // TODO(cache): Also check that the root's vrgid matches what's in the cache
                        if (invalidateEntry || vnodeVid != this->entries[insertionIndex].vid)
                        {
                            this->UpdateIndexEntryToLatest_Locked(
                                context,
                                perfTracer,
                                cacheMissFallbackFunctionCounter,
                                cacheMissFallbackFunctionInnerLoopCounter,
                                insertionIndex,
                                vnode,
                                vnodeVid);
                        }
                        
                        rootHandle = this->entries[insertionIndex].virtualizationRoot;
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
        RWLock_ReleaseExclusive(this->entriesLock);
    }
    else
    {
        RWLock_ReleaseShared(this->entriesLock);
    }
    
    return rootHandle;
}

uintptr_t VnodeCache::HashVnode(vnode_t vnode)
{
    uintptr_t vnodeAddress = reinterpret_cast<uintptr_t>(vnode);
    return (vnodeAddress >> 3) % this->capacity;
}

bool VnodeCache::TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, /* out */  uintptr_t& cacheIndex)
{
    return this->TryFindVnodeIndex_Locked(vnode, startingIndex, startingIndex, cacheIndex);
}

bool VnodeCache::TryFindVnodeIndex_Locked(vnode_t vnode, uintptr_t startingIndex, uintptr_t stoppingIndex, /* out */  uintptr_t& cacheIndex)
{
    // Walk from the starting index until we find:
    //    -> The vnode
    //    -> A NULLVP entry
    //    -> The stopping index
    // If we hit the end of the array, continue searching from the start
    cacheIndex = startingIndex;
    while (vnode != this->entries[cacheIndex].vnode)
    {
        if (NULLVP == this->entries[cacheIndex].vnode)
        {
            return true;
        }
    
        cacheIndex = (cacheIndex + 1) % this->capacity;
        if (cacheIndex == stoppingIndex)
        {
            // Looped through the entire cache and didn't find an empty slot or the vnode
            return false;
        }
    }
    
    return true;
}

void VnodeCache::UpdateIndexEntryToLatest_Locked(
    vfs_context_t context,
    PerfTracer* perfTracer,
    PrjFSPerfCounter cacheMissFallbackFunctionCounter,
    PrjFSPerfCounter cacheMissFallbackFunctionInnerLoopCounter,
    uintptr_t index,
    vnode_t vnode,
    uint32_t vnodeVid)
{
    FsidInode vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context);
    
    this->entries[index].vnode = vnode;
    this->entries[index].vid = vnodeVid;
    
    // TODO(cache): Add proper perf points
    this->entries[index].virtualizationRoot = VirtualizationRoot_FindForVnode(
        perfTracer,
        cacheMissFallbackFunctionCounter,
        cacheMissFallbackFunctionInnerLoopCounter,
        vnode,
        vnodeFsidInode);

    // TODO(cache): Also set the vrgid
}
