#import <XCTest/XCTest.h>

#include "../PrjFSKext/ArrayUtilities.hpp"

@interface ArrayUtilitiesTests : XCTestCase
@end

@implementation ArrayUtilitiesTests

struct TestStruct
{
    int data1;
    double data2;
};

static void* length1PointerArray[1];
static char length3CharArray[3];
static uint32_t length5Uint32Array[] = { 1, 2, 3, 4, 5};
static TestStruct length6TestStructArray[6];

- (void)testArraySize {
    XCTAssertTrue(1 == Array_Size(length1PointerArray));
    XCTAssertTrue(3 == Array_Size(length3CharArray));
    XCTAssertTrue(5 == Array_Size(length5Uint32Array));
    XCTAssertTrue(6 == Array_Size(length6TestStructArray));
}

@end
