#include "../PrjFSKext/Memory.hpp"

void Memory_Free(void* memory, uint32_t sizeBytes)
{
    free(memory);
}

void* Memory_Alloc(uint32_t sizeBytes)
{
    return malloc(sizeBytes);
}

void* Memory_AllocNoBlock(uint32_t size)
{
    return Memory_Alloc(size);
}
