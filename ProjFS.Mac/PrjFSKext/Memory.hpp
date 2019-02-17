#ifndef Memory_h
#define Memory_h

#include <kern/assert.h>

kern_return_t Memory_Init();
kern_return_t Memory_Cleanup();

void* Memory_Alloc(uint32_t size);
void Memory_Free(void* buffer, uint32_t size);

inline void* operator new(size_t size, void* memory)
{
    return memory;
}

template <typename T>
T* Memory_AllocArray(uint32_t arrayLength)
{
    size_t allocBytes = arrayLength * sizeof(T);
    if (allocBytes > UINT32_MAX)
    {
        return nullptr;
    }
    
    T* array = static_cast<T*>(Memory_Alloc(static_cast<uint32_t>(allocBytes)));
    for (uint32_t i = 0; i < arrayLength; ++i)
    {
        new(&array[i]) T();
    }
    
    return array;
}

template <typename T>
void Memory_FreeArray(T* array, uint32_t arrayLength)
{
    for (uint32_t i = 0; i < arrayLength; ++i)
    {
        array[i].~T();
    }
    
    size_t arrayBytes = arrayLength * sizeof(T);
    assert(arrayBytes <= UINT32_MAX);
    Memory_Free(array, static_cast<uint32_t>(arrayBytes));
}

#endif /* Memory_h */
