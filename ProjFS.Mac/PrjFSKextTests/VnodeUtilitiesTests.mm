#include "../PrjFSKext/VnodeUtilitiesTestable.hpp"
#import <XCTest/XCTest.h>

@interface VnodeUtilitiesTests : XCTestCase

@end

@implementation VnodeUtilitiesTests

- (void)setUp
{
}

- (void)tearDown
{
}

- (void)testTruncatePathToParent
{
    char path[PrjFSMaxPath] = "/foo/bar";
    TruncatePathToParent(path);
    XCTAssertEqual(0, strcmp(path, "/foo"));
}

@end
