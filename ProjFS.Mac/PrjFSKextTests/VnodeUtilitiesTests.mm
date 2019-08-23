#include "VnodeUtilities.hpp"
#include "MockVnodeAndMount.hpp"
#import <XCTest/XCTest.h>

using std::shared_ptr;

@interface VnodeUtilitiesTests : XCTestCase

@end

@implementation VnodeUtilitiesTests

- (void)setUp
{
    [super setUp];
}

- (void)tearDown
{
    [super tearDown];
}

- (void)testVnodeGetTypeAsString
{
    // Ensure NULL vnode case is handled correctly
    XCTAssertEqual(0, strcmp("[NULL]", Vnode_GetTypeAsString(NULLVP)));
    
    // VREG, VDIR, and VLNK are the most useful for our purposes in practice
    shared_ptr<vnode> testVnode = vnode::Create(nullptr, "/foo", VREG);
    XCTAssertEqual(0, strcmp("VREG", Vnode_GetTypeAsString(testVnode.get())));
    testVnode->SetVnodeType(VDIR);
    XCTAssertEqual(0, strcmp("VDIR", Vnode_GetTypeAsString(testVnode.get())));
    testVnode->SetVnodeType(VLNK);
    XCTAssertEqual(0, strcmp("VLNK", Vnode_GetTypeAsString(testVnode.get())));
    
    // Verify that we always get back some kind of sensible string, even if we go out of bounds
    static_assert(VNON == 0, "We want to start iterating at 0");
    for (unsigned vtype = VNON; vtype < VCPLX + 10; ++vtype)
    {
        testVnode->SetVnodeType(static_cast<enum vtype>(vtype));
        const char* vtypeString = Vnode_GetTypeAsString(testVnode.get());
        XCTAssertNotEqual(vtypeString, nullptr);
        XCTAssertGreaterThan(strlen(vtypeString), 0);
    }
}

@end
