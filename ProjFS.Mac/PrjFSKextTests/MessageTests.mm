#import <XCTest/XCTest.h>

#include "../PrjFSKext/Message_Kernel.hpp"

@interface MessageTests : XCTestCase

@end

@implementation MessageTests

- (void)setUp
{
}

- (void)tearDown
{
}

- (void)testMessageEncodedSize
{
    MessageHeader testHeader = {};
    XCTAssertEqual(sizeof(testHeader), Message_EncodedSize(&testHeader));
    
    testHeader.pathSizesBytes[MessagePath_Target] = UINT16_MAX;
    testHeader.pathSizesBytes[MessagePath_From] = UINT16_MAX;
    
    static const uint64_t maxSize = sizeof(testHeader) + UINT16_MAX * 2llu;
    XCTAssertEqual(maxSize, Message_EncodedSize(&testHeader));
    
    srand(0);
    for (unsigned i = 0; i < 10000; ++i)
    {
        uint16_t targetSize = testHeader.pathSizesBytes[MessagePath_Target] = rand() & UINT16_MAX;
        uint16_t fromSize = testHeader.pathSizesBytes[MessagePath_From] = rand() & UINT16_MAX;
        uint32_t size = Message_EncodedSize(&testHeader);
        XCTAssertGreaterThanOrEqual(size, sizeof(testHeader));
        XCTAssertLessThanOrEqual(size, maxSize);
        XCTAssertGreaterThan(size, targetSize);
        XCTAssertGreaterThan(size, fromSize);
    }
    
}

@end
