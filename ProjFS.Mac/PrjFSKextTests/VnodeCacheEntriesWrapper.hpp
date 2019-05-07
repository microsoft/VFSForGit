#pragma once

#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/VnodeCachePrivate.hpp"
#include "../PrjFSKext/VnodeCacheTestable.hpp"

// Helper class for interacting with s_entries, s_entriesCapacity, and s_ModBitmask
class VnodeCacheEntriesWrapper
{
public:
    VnodeCacheEntriesWrapper()
    {
        s_entries = nullptr;
    }
    
    ~VnodeCacheEntriesWrapper()
    {
        this->FreeCache();
    }
    
    void AllocateCache()
    {
        s_entriesCapacity = 64;
        s_ModBitmask = s_entriesCapacity - 1;
        s_entries = new VnodeCacheEntry[s_entriesCapacity];
        
        for (uint32_t i = 0; i < s_entriesCapacity; ++i)
        {
            memset(&(s_entries[i]), 0, sizeof(VnodeCacheEntry));
        }
    }
    
    void FreeCache()
    {
        s_entriesCapacity = 0;
        
        if (nullptr != s_entries)
        {
            delete[] s_entries;
            s_entries = nullptr;
        }
        
        this->dummyMount.reset();
        this->dummyNode.reset();
    }
    
    void FillAllEntries()
    {
        // Keep these dummy instances alive until FreeCache is called to ensure that
        // no subsequent allocations overlap with the vnode we're using to fill the cache
        this->dummyMount = mount::Create();
        this->dummyNode = dummyMount->CreateVnodeTree("/DUMMY");
        
        for (uint32_t i = 0; i < s_entriesCapacity; ++i)
        {
            s_entries[i].vnode = this->dummyNode.get();
        }
    }
    
    void MarkEntryAsFree(const uintptr_t entryIndex)
    {
        s_entries[entryIndex].vnode = nullptr;
    }
    
    VnodeCacheEntry& operator[] (const uintptr_t entryIndex)
    {
        return s_entries[entryIndex];
    }
    
    uint32_t GetCapacity() const
    {
        return s_entriesCapacity;
    }
    
private:
    std::shared_ptr<mount> dummyMount;
    std::shared_ptr<vnode> dummyNode;
};
