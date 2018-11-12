#ifndef Memory_h
#define Memory_h

kern_return_t Memory_Init();
kern_return_t Memory_Cleanup();

void* Memory_Alloc(uint32_t size);
// Variant which is safe to call while lock is held, but with low chance of success.
void* Memory_AllocNoBlock(uint32_t size);
void Memory_Free(void* buffer, uint32_t size);

#ifdef KERNEL
// placement new (normally in the C++ standard libary, but we don't have one)
inline void* operator new(size_t size, void* memory) noexcept
{
    return memory;
}
#endif

template <typename T>
T* Memory_AllocArray(uint32_t arrayLength)
{
    size_t allocBytes = arrayLength * sizeof(T);
    if (allocBytes > UINT32_MAX)
    {
        return nullptr;
    }
    
    return static_cast<T*>(Memory_Alloc(static_cast<uint32_t>(allocBytes)));
}


#endif /* Memory_h */
