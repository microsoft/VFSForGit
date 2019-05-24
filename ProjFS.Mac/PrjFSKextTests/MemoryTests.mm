#import "KextAssertIntegration.h"
#include "../PrjFSKext/Memory.hpp"

@interface MemoryTests : PFSKextTestCase

@end

@implementation MemoryTests

- (void)testAllocArrayReturnsNullOnOverflow {
    long* array = Memory_AllocArray<long>(UINT32_MAX);
    XCTAssert(array == nullptr);
}

- (void)testAllocArrayWorksOnNoOverflow {
    long* array = Memory_AllocArray<long>(10);
    XCTAssert(array != nullptr);
    Memory_FreeArray(array, 10);
}

@end
