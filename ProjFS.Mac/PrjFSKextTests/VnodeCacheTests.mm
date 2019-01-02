#import <XCTest/XCTest.h>

typedef int16_t VirtualizationRootHandle;

#include "../PrjFSKext/ArrayUtilities.hpp"
#include "../PrjFSKext/VnodeCache.hpp"
#include "../PrjFSKext/VnodeCacheTestable.hpp"
#include "../PrjFSKext/VnodeCachePrivate.hpp"

@interface VnodeCacheTests : XCTestCase
@end

@implementation VnodeCacheTests

// Dummy vfs_context implementation for vfs_context_t
struct vfs_context
{
};

// Dummy vnode implementation for vnode_t
struct vnode
{
    int dummyData;
};

static vnode TestVnode;
static const VirtualizationRootHandle TestRootHandle = 1;

static void AllocateCacheEntries(uint32_t capacity, bool fillCache);
static void FreeCacheEntries();
static void MarkEntryAsFree(uintptr_t entryIndex);

- (void)testComputePow2CacheCapacity {

    // At a minimum ComputePow2CacheCapacity should return the minimum value in AllowedPow2CacheCapacities
    XCTAssertTrue(AllowedPow2CacheCapacities[0] == ComputePow2CacheCapacity(0));
    
    // ComputePow2CacheCapacity should round up to the nearest power of 2 (after multiplying expectedVnodeCount by 2)
    int expectedVnodeCount = AllowedPow2CacheCapacities[0]/2 + 1;
    XCTAssertTrue(AllowedPow2CacheCapacities[1] == ComputePow2CacheCapacity(expectedVnodeCount));
    
    // ComputePow2CacheCapacity should be capped at the maximum value in AllowedPow2CacheCapacities
    size_t lastAllowedSizeIndex = Array_Size(AllowedPow2CacheCapacities) - 1;
    XCTAssertTrue(AllowedPow2CacheCapacities[lastAllowedSizeIndex] == ComputePow2CacheCapacity(AllowedPow2CacheCapacities[lastAllowedSizeIndex]));
}

- (void)testComputeVnodeHashKeyWithCapacityOfOne {
    s_entriesCapacity = 1;
    vnode testVnode2;
    vnode testVnode3;
    
    XCTAssertTrue(0 == ComputeVnodeHashIndex(&TestVnode));
    XCTAssertTrue(0 == ComputeVnodeHashIndex(&testVnode2));
    XCTAssertTrue(0 == ComputeVnodeHashIndex(&testVnode3));
}

- (void)testVnodeCache_InvalidateCache_SetsMemoryToZeros {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);
    VnodeCacheEntry* emptyArray = static_cast<VnodeCacheEntry*>(calloc(s_entriesCapacity, sizeof(VnodeCacheEntry)));
    XCTAssertTrue(0 != memcmp(emptyArray, s_entries, sizeof(VnodeCacheEntry) * s_entriesCapacity));
    
    PerfTracer dummyPerfTracer;
    VnodeCache_InvalidateCache(&dummyPerfTracer);
    XCTAssertTrue(0 == memcmp(emptyArray, s_entries, sizeof(VnodeCacheEntry)*s_entriesCapacity));
    
    free(emptyArray);
    FreeCacheEntries();
}

- (void)testInvalidateCache_ExclusiveLocked_SetsMemoryToZeros {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);
    VnodeCacheEntry* emptyArray = static_cast<VnodeCacheEntry*>(calloc(s_entriesCapacity, sizeof(VnodeCacheEntry)));
    XCTAssertTrue(0 != memcmp(emptyArray, s_entries, sizeof(VnodeCacheEntry) * s_entriesCapacity));
    
    InvalidateCache_ExclusiveLocked();
    XCTAssertTrue(0 == memcmp(emptyArray, s_entries, sizeof(VnodeCacheEntry)*s_entriesCapacity));
    
    free(emptyArray);
    FreeCacheEntries();
}

- (void)testTryFindVnodeIndex_SharedLocked_ReturnsStartingIndexWhenNull {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ false);
    
    vnode_t testVnode = &TestVnode;
    uintptr_t startingIndex = 5;
    uintptr_t cacheIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(testVnode, startingIndex, /* out */ cacheIndex));
    XCTAssertTrue(cacheIndex == startingIndex);
    
    FreeCacheEntries();
}

- (void)testTryFindVnodeIndex_SharedLocked_ReturnsFalseWhenCacheFull {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);
    
    vnode_t testVnode = &TestVnode;
    uintptr_t startingIndex = 5;
    uintptr_t cacheIndex;
    XCTAssertFalse(TryFindVnodeIndex_Locked(testVnode, startingIndex, /* out */ cacheIndex));
    
    FreeCacheEntries();
}

- (void)testTryFindVnodeIndex_SharedLocked_WrapsToBeginningWhenResolvingCollisions {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);
    
    uintptr_t emptyIndex = 2;
    MarkEntryAsFree(emptyIndex);
    
    vnode_t testVnode = &TestVnode;
    uintptr_t startingIndex = 5;
    uintptr_t cacheIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(testVnode, startingIndex, /* out */ cacheIndex));
    XCTAssertTrue(emptyIndex == cacheIndex);
    
    FreeCacheEntries();
}

- (void)testTryFindVnodeIndex_SharedLocked_ReturnsLastIndexWhenEmptyAndResolvingCollisions {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);
    uintptr_t emptyIndex = s_entriesCapacity - 1;
    MarkEntryAsFree(emptyIndex);
    
    vnode_t testVnode = &TestVnode;
    uintptr_t startingIndex = 5;
    uintptr_t cacheIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(testVnode, startingIndex, /* out */ cacheIndex));
    XCTAssertTrue(emptyIndex == cacheIndex);
    
    FreeCacheEntries();
}

- (void)testTryInsertOrUpdateEntry_ExclusiveLocked_ReturnsFalseWhenFull {
    AllocateCacheEntries(/* capacity*/ 100, /* fillCache*/ true);

    uintptr_t indexFromHash = 5;
    vnode_t testVnode = &TestVnode;
    uint32_t testVnodeVid = 7;

    XCTAssertFalse(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            testVnode,
            indexFromHash,
            testVnodeVid,
            true, // invalidateEntry
            TestRootHandle));

    FreeCacheEntries();
}

static void AllocateCacheEntries(uint32_t capacity, bool fillCache)
{
    s_entriesCapacity = capacity;
    s_entries = new VnodeCacheEntry[s_entriesCapacity];
    
    static vnode dummyNode;
    for (uint32_t i = 0; i < s_entriesCapacity; ++i)
    {
        if (fillCache)
        {
            s_entries[i].vnode = &dummyNode;
        }
        else
        {
            memset(&(s_entries[i]), 0, sizeof(VnodeCacheEntry));
        }
    }
}

static void FreeCacheEntries()
{
    s_entriesCapacity = 0;
    delete[] s_entries;
}

static void MarkEntryAsFree(uintptr_t entryIndex)
{
    s_entries[entryIndex].vnode = nullptr;
}

@end
