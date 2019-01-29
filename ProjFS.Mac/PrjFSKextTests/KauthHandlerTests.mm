#import <XCTest/XCTest.h>
#include "../PrjFSKext/KauthHandlerTestable.hpp"
#include <sys/vnode.h>

@interface KauthHandlerTests : XCTestCase
@end

@implementation KauthHandlerTests

//Mock for TryReadVNodeFileFlags
static uint32_t mockFileFlag;
static bool mockReturnBool;
bool TryReadVNodeFileFlags(vnode_t vn, vfs_context_t context, uint32_t* flags);
bool TryReadVNodeFileFlags(vnode_t vn, vfs_context_t context, uint32_t* flags)
{
    *flags = mockFileFlag;
    return mockReturnBool;
}

- (void)testActionBitIsSet {
    XCTAssertTrue(ActionBitIsSet(KAUTH_VNODE_READ_DATA, KAUTH_VNODE_READ_DATA));
    XCTAssertTrue(ActionBitIsSet(KAUTH_VNODE_WRITE_DATA, KAUTH_VNODE_WRITE_DATA));
    XCTAssertTrue(ActionBitIsSet(KAUTH_VNODE_WRITE_DATA, KAUTH_VNODE_READ_DATA | KAUTH_VNODE_WRITE_DATA));
    XCTAssertTrue(ActionBitIsSet(KAUTH_VNODE_READ_DATA | KAUTH_VNODE_WRITE_DATA, KAUTH_VNODE_WRITE_DATA));
    XCTAssertFalse(ActionBitIsSet(KAUTH_VNODE_WRITE_DATA, KAUTH_VNODE_READ_DATA));
}

- (void)testIsFileSystemCrawler {
    XCTAssertTrue(IsFileSystemCrawler("mds"));
    XCTAssertTrue(IsFileSystemCrawler("mdworker"));
    XCTAssertTrue(IsFileSystemCrawler("mds_stores"));
    XCTAssertTrue(IsFileSystemCrawler("fseventsd"));
    XCTAssertTrue(IsFileSystemCrawler("Spotlight"));
    XCTAssertFalse(IsFileSystemCrawler("mds_"));
    XCTAssertFalse(IsFileSystemCrawler("spotlight"));
    XCTAssertFalse(IsFileSystemCrawler("git"));
}

- (void)testFileFlagsBitIsSet {
    XCTAssertTrue(FileFlagsBitIsSet(FileFlags_IsEmpty, FileFlags_IsEmpty));
    XCTAssertTrue(FileFlagsBitIsSet(FileFlags_IsInVirtualizationRoot, FileFlags_IsInVirtualizationRoot));
    XCTAssertFalse(FileFlagsBitIsSet(FileFlags_IsInVirtualizationRoot, FileFlags_IsEmpty));
    XCTAssertFalse(FileFlagsBitIsSet(FileFlags_IsInVirtualizationRoot, FileFlags_Invalid));
}

- (void)testShouldIgnoreVnodeType {
    XCTAssertTrue(ShouldIgnoreVnodeType(VNON, NULL));
    XCTAssertTrue(ShouldIgnoreVnodeType(VBLK, NULL));
    XCTAssertTrue(ShouldIgnoreVnodeType(VCHR, NULL));
    XCTAssertTrue(ShouldIgnoreVnodeType(VSOCK, NULL));
    XCTAssertTrue(ShouldIgnoreVnodeType(VFIFO, NULL));
    XCTAssertTrue(ShouldIgnoreVnodeType(VBAD, NULL));
    XCTAssertFalse(ShouldIgnoreVnodeType(VREG, NULL));
    XCTAssertFalse(ShouldIgnoreVnodeType(VDIR, NULL));
    XCTAssertFalse(ShouldIgnoreVnodeType(VLNK, NULL));
    XCTAssertFalse(ShouldIgnoreVnodeType(VSTR, NULL));
    XCTAssertFalse(ShouldIgnoreVnodeType(VCPLX, NULL));
}

- (void)testFileFlaggedInRoot {
    bool fileFlaggedInRoot;
    mockReturnBool = false;
    mockFileFlag = FileFlags_IsInVirtualizationRoot;
    XCTAssertFalse(TryGetFileIsFlaggedAsInRoot(reinterpret_cast<vnode_t>(static_cast<uintptr_t>(1)), NULL, &fileFlaggedInRoot));

    mockReturnBool = true;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(reinterpret_cast<vnode_t>(static_cast<uintptr_t>(1)), NULL, &fileFlaggedInRoot));
    XCTAssertTrue(fileFlaggedInRoot);

    mockFileFlag = FileFlags_IsEmpty;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(reinterpret_cast<vnode_t>(static_cast<uintptr_t>(1)), NULL, &fileFlaggedInRoot));
    XCTAssertFalse(fileFlaggedInRoot);
}

@end
