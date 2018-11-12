#import <XCTest/XCTest.h>
#include <list>
#import "../PrjFSKext/AssociativeArray.hpp"

@interface AssociativeArrayTests : XCTestCase

@end

static AssociativeArrayTests* s_test;
static bool s_failNonblockingAlloc = false;

struct TestArrayElement
{
    uint32_t key;
    uint32_t value;
};

static bool TestArrayElementKeyEquals(const uint32_t& key, const TestArrayElement& item)
{
    return item.key == key;
}

struct MemoryHeader;
typedef std::list<MemoryHeader*> AllocationList;

struct MemoryHeader
{
    uint32_t size;
    AllocationList::iterator listEntry;
    alignas(16) uint8_t allocatedBytes[];
};
static_assert(offsetof(MemoryHeader, allocatedBytes) == sizeof(MemoryHeader), "Bytes should be at end of struct");

static AllocationList s_allocationList;

void* Memory_Alloc(uint32_t size)
{
   void* memory = malloc(sizeof(MemoryHeader) + size);
   MemoryHeader* header = new(memory) MemoryHeader();
   header->size = size;
   AllocationList::iterator inserted = s_allocationList.insert(s_allocationList.end(), header);
   header->listEntry = inserted;
   return header->allocatedBytes;
}

void Memory_Free(void* memory, uint32_t size)
{
    AssociativeArrayTests* self = s_test;
    
    XCTAssertNotEqual(memory, nullptr);
    void* mallocedMemory = static_cast<uint8_t*>(memory) - offsetof(MemoryHeader, allocatedBytes);
    MemoryHeader* header = static_cast<MemoryHeader*>(mallocedMemory);
    XCTAssertEqual(header->size, size);
    
    s_allocationList.erase(header->listEntry);
    
    free(mallocedMemory);
}

void* Memory_AllocNoBlock(uint32_t size)
{
    if (s_failNonblockingAlloc)
    {
        return nullptr;
    }
    else
    {
        return Memory_Alloc(size);
    }
}

void RWLock_AcquireExclusive(RWLock& rwLock)
{
    // TODO(Mac): Verify lock usage
}
void RWLock_ReleaseExclusive(RWLock& rwLock)
{
}

@implementation AssociativeArrayTests

- (void)setUp {
    [super setUp];
    // Put setup code here. This method is called before the invocation of each test method in the class.
    s_test = self;
}

- (void)tearDown
{
    [self checkMemoryAllocations];

    XCTAssertEqualObjects(s_test, self);
    s_test = nil;
    // Put teardown code here. This method is called after the invocation of each test method in the class.
    [super tearDown];
}

- (void)checkMemoryAllocations
{
    XCTAssertTrue(s_allocationList.empty());
}

- (void)testFindOrInsertLockedAllowResize
{
    RWLock dummyMutex;
    AssociativeArray<uint32_t, TestArrayElement, TestArrayElementKeyEquals> array;
    TestArrayElement* inserted = array.FindOrInsertLockedAllowResize(1, TestArrayElement { 1, 100 }, dummyMutex);
    XCTAssertNotEqual(inserted, nullptr);
    XCTAssertEqual(inserted->key, 1);
    XCTAssertEqual(inserted->value, 100);

    TestArrayElement* found = array.FindOrInsertLockedAllowResize(1, TestArrayElement { 1, 200 }, dummyMutex);
    XCTAssertNotEqual(found, nullptr);
    XCTAssertEqual(found->key, 1);
    XCTAssertEqual(found->value, 100, "Should find previously inserted item, not inserted a new one");
}

- (void)testFindOrInsertLockedAllowResizeWithFailingNonblock
{
    s_failNonblockingAlloc = true;
    
    RWLock dummyMutex;
    AssociativeArray<uint32_t, TestArrayElement, TestArrayElementKeyEquals> array;
    TestArrayElement* inserted = array.FindOrInsertLockedAllowResize(1, TestArrayElement { 1, 100 }, dummyMutex);
    XCTAssertNotEqual(inserted, nullptr);
    XCTAssertEqual(inserted->key, 1);
    XCTAssertEqual(inserted->value, 100);

    TestArrayElement* found = array.FindOrInsertLockedAllowResize(1, TestArrayElement { 1, 200 }, dummyMutex);
    XCTAssertNotEqual(found, nullptr);
    XCTAssertEqual(found->key, 1);
    XCTAssertEqual(found->value, 100, "Should find previously inserted item, not inserted a new one");
}

@end
