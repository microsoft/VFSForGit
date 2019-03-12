#ifndef Memory_h
#define Memory_h

#include <mach/kern_return.h>

#if !defined(KEXT_UNIT_TESTING) || defined(TESTABLE_KEXT_TARGET) // Building kext
#include <kern/assert.h>
#else // building unit tests/mocks
#include <cassert>
#endif

kern_return_t Memory_Init();
kern_return_t Memory_Cleanup();

void* Memory_Alloc(uint32_t size);
void Memory_Free(void* buffer, uint32_t size);

template <typename T>
T* Memory_AllocArray(uint32_t arrayLength)
{
    uint32_t allocBytes;
    if (__builtin_umul_overflow(arrayLength, sizeof(T), &allocBytes))
    {
        // Overflow occurred.
        return nullptr;
    }

    return static_cast<T*>(Memory_Alloc(allocBytes));
}

template <typename T>
void Memory_FreeArray(T* array, uint32_t arrayLength)
{
    uint32_t arrayBytes;
    if (__builtin_umul_overflow(arrayLength, sizeof(T), &arrayBytes))
    {
        // This should never occur, since Memory_AllocArray should not allow the initial computation to overflow.
        assert(!"Overflow detected: was this array allocated through Memory_AllocArray?");
    }
    else
    {
        Memory_Free(array, arrayBytes);
    }
}

#endif /* Memory_h */
