#pragma once

#include "Locks.hpp"
#include "Memory.hpp"
#include "KextCppUtilities.hpp"

struct ResizeResult
{
    bool   memoryWasUsed;
    uint32_t releasedMemorySize;
    void*  releasedMemory;
};

template <typename KEY, typename VALUE, bool EQUAL_FN(const KEY&, const VALUE&)>
class AssociativeArray
{
    AssociativeArray(const AssociativeArray&) = delete;
    AssociativeArray& operator=(const AssociativeArray&) = delete;

    uint32_t capacity;
    uint32_t numberUsed;
    VALUE* data;
    
    uint32_t NextGrowCapacity() const
    {
        return (this->capacity == 0) ? 8u : (this->capacity * 2u);
    }
    
public:
    AssociativeArray() :
        capacity(0),
        numberUsed(0),
        data(nullptr)
    {}
    
    ~AssociativeArray()
    {
        this->ClearAndFreeMemory();
    }
    
    void Clear()
    {
        for (uint32_t i = 0; i < this->numberUsed; ++i)
        {
            this->data[i].~VALUE();
        }
        this->numberUsed = 0;
    }
    
    void ClearAndFreeMemory()
    {
        this->Clear();
        
        if (nullptr != this->data)
        {
            Memory_Free(this->data, MemorySizeForCapacity(this->capacity));
            this->data = nullptr;
            this->capacity = 0;
        }
    }
    
    uint32_t MemorySizeToGrow() const
    {
        return this->NextGrowCapacity() * sizeof(this->data[0]);
    }
    
    static uint32_t MemorySizeForCapacity(uint32_t capacity)
    {
        return capacity * sizeof(VALUE);
    }
    
    ResizeResult GrowUsingMemory(void* memory, uint32_t memorySize)
    {
        uint32_t expectedCapacity = this->NextGrowCapacity();
        uint32_t expectedMemorySize = this->MemorySizeToGrow();
        if (memorySize != expectedMemorySize)
        {
            return ResizeResult{ false };
        }
        
        return this->ReserveCapacity(expectedCapacity, memory, memorySize);
    }

    ResizeResult ReserveCapacity(uint32_t newCapacity, void* memory, uint32_t memorySize)
    {
        assert(MemorySizeForCapacity(newCapacity) == memorySize);
        assert(newCapacity >= this->numberUsed);
        
        uint8_t* memoryPosition = static_cast<uint8_t*>(memory);
        for (size_t i = 0; i < this->numberUsed; ++i, memoryPosition += sizeof(VALUE))
        {
            VALUE* moved = new(memoryPosition) VALUE(KextCpp::move(this->data[i]));
            (void)moved;
            this->data[i].~VALUE();
        }
        
        ResizeResult result = { true, MemorySizeForCapacity(this->capacity), this->data };
        this->data = static_cast<VALUE*>(memory);
        this->capacity = memorySize / sizeof(VALUE);
        return result;
    }
    
    VALUE* Find(KEY key)
    {
        for (size_t i = 0; i < this->numberUsed; ++i)
        {
            if (EQUAL_FN(key, this->data[i]))
            {
                return &this->data[i];
            }
        }
        
        return nullptr;
    }
    
    // Inserts a new item, if there is space. No checks for duplicates are done.
    VALUE* TryInsert(VALUE&& newItem)
    {
        size_t nextIndex = this->numberUsed;
        if (nextIndex < this->capacity)
        {
            ++this->numberUsed;
            VALUE* inserted = new(&this->data[nextIndex]) VALUE(newItem);
            assert(inserted == &this->data[nextIndex]);
            return inserted;
        }
        else
        {
            return nullptr;
        }
    }
    
    VALUE* FindOrTryInsert(KEY key, VALUE&& newItem)
    {
        VALUE* value = this->Find(key);
        if (nullptr == value)
        {
            value = this->TryInsert(KextCpp::move(newItem));
        }
        return value;
    }
    
    void Remove(VALUE* item)
    {
        assert(item != nullptr);
        assert(item >= this->data && item < this->data + this->capacity);
        assert(this->numberUsed > 0);
        
        // move the last item into the liberated slot if not removing the last item
        VALUE* lastItem = this->data + (this->numberUsed - 1);
        if (lastItem != item)
        {
            *item = KextCpp::move(*lastItem);
        }
        lastItem->~VALUE();

        --this->numberUsed;
    }
    
    // Find item matching key; if not found, insert newItem, growing the array if necessary
    // (during which the lock may be dropped and then re-acquired (exclusively))
    // Returns the found or inserted value, or nullptr if resizing failed.
    VALUE* FindOrInsertLockedAllowResize(KEY key, VALUE&& newItem, RWLock& lock)
    {
        while (true)
        {
            VALUE* value = this->FindOrTryInsert(key, KextCpp::move(newItem));
            if (nullptr != value)
            {
                return value;
            }
        
            uint32_t memoryNeeded = this->MemorySizeToGrow();
            void* reservedMemory = Memory_AllocNoBlock(memoryNeeded);
            if (nullptr == reservedMemory)
            {
                RWLock_ReleaseExclusive(lock);
                {
                    reservedMemory = Memory_Alloc(memoryNeeded);
                }
                RWLock_AcquireExclusive(lock);
            }
            
            if (nullptr == reservedMemory)
            {
                // Memory allocation failed, try last-ditch attempt to find/insert
                return this->FindOrTryInsert(key, KextCpp::move(newItem));
            }
            
            ResizeResult resizeResult = this->GrowUsingMemory(reservedMemory, memoryNeeded);
            
            if (!resizeResult.memoryWasUsed)
            {
                Memory_Free(reservedMemory, memoryNeeded);
            }
            
            if (nullptr != resizeResult.releasedMemory)
            {
                Memory_Free(resizeResult.releasedMemory, resizeResult.releasedMemorySize);
            }
        }
    }
};
