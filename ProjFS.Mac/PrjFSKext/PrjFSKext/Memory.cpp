#include <kern/debug.h>
#include <libkern/libkern.h>
#include <libkern/OSMalloc.h>

#include "PrjFSCommon.h"
#include "Memory.hpp"

static OSMallocTag s_mallocTag = nullptr;

kern_return_t Memory_Init()
{
    if (nullptr != s_mallocTag)
    {
        return KERN_FAILURE;
    }
    
    s_mallocTag = OSMalloc_Tagalloc(PrjFSKextBundleId, OSMT_DEFAULT);
    if (nullptr == s_mallocTag)
    {
        return KERN_FAILURE;
    }
    
    return KERN_SUCCESS;
}

kern_return_t Memory_Cleanup()
{
    if (nullptr != s_mallocTag)
    {
        OSMalloc_Tagfree(s_mallocTag);
        s_mallocTag = nullptr;
        
        return KERN_SUCCESS;
    }
    
    return KERN_FAILURE;
}

void* Memory_Alloc(uint32_t size)
{
    return OSMalloc(size, s_mallocTag);
}

void* Memory_AllocNoBlock(uint32_t size)
{
    return OSMalloc_noblock(size, s_mallocTag);
}

void Memory_Free(void* buffer, uint32_t size)
{
    OSFree(buffer, size, s_mallocTag);
}
