#pragma once

#import <XCTest/XCTest.h>

// XCTestCase subclass that registers itself with the kext assert integration system
@interface PFSKextTestCase : XCTestCase

- (void) setUp;
- (void) tearDown;

- (void) setExpectedFailedKextAssertionCount:(uint32_t)failedAssertionCount;

@end
