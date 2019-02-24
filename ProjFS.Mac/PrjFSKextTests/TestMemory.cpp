#include "../PrjFSKext/Memory.hpp"

void Memory_Free(void* memory, uint32_t sizeBytes)
{
    free(memory);
}

void* Memory_Alloc(uint32_t sizeBytes)
{
    return malloc(sizeBytes);
}
