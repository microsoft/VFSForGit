#include "../PrjFSKext/Memory.hpp"
#include <cstddef>

struct TrackedKextMemoryAllocation
{
    uint32_t size;
    uint8_t data[];
};

void Memory_Free(void* memory, uint32_t sizeBytes)
{
    TrackedKextMemoryAllocation* memoryObject = reinterpret_cast<TrackedKextMemoryAllocation*>(static_cast<uint8_t*>(memory) - offsetof(TrackedKextMemoryAllocation, data));
    assert(memoryObject->size == sizeBytes);
    free(memoryObject);
}

void* Memory_Alloc(uint32_t sizeBytes)
{
    TrackedKextMemoryAllocation* memoryObject = static_cast<TrackedKextMemoryAllocation*>(malloc(sizeBytes + sizeof(TrackedKextMemoryAllocation)));
    memoryObject->size = sizeBytes;
    return memoryObject->data;
}
