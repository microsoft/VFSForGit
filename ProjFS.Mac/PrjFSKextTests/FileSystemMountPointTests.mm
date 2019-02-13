#include "../PrjFSKext/FileSystemMountPoints.hpp"
#include "../PrjFSKext/FileSystemMountPointsTestable.hpp"
#include "MockVnodeAndMount.hpp"
#include "KextMockUtilities.hpp"
#import <XCTest/XCTest.h>
#include <array>

using std::shared_ptr;
using std::array;
using std::generate;


@interface FileSystemMountPointTests : XCTestCase

@end

@implementation FileSystemMountPointTests
{
    array<shared_ptr<mount>, MaxUsedMountPoints> dummyMounts;
}

- (void) fillAllMountPointSlots
{
    for (unsigned i = 0; i < MaxUsedMountPoints; ++i)
    {
        s_usedMountPoints[i].authCacheDisableCount = 1;
        s_usedMountPoints[i].mountPoint = self->dummyMounts[i].get();
    }
}

- (void) fillAllMountPointSlotsExcept:(unsigned)leaveEmptyIndex
{
    for (unsigned i = 0; i < MaxUsedMountPoints; ++i)
    {
        if (i != leaveEmptyIndex)
        {
            s_usedMountPoints[i].authCacheDisableCount = 1;
            s_usedMountPoints[i].mountPoint = self->dummyMounts[i].get();
        }
    }
}


- (void)setUp
{
    srand(0);
    memset(s_usedMountPoints, 0, sizeof(s_usedMountPoints));
    
    // This is roughly what "real" fsids look like
    fsid_t testMountFsid = { (1 << 24) | (rand() % 32), (rand() % 16) };
    
    for (shared_ptr<mount>& dummy : self->dummyMounts)
    {
        dummy = mount::Create("hfs", testMountFsid, rand() /* initial inode */);
        testMountFsid.val[1]++;
    }

    MountPoint_Init();
}

- (void)tearDown
{
    MountPoint_Cleanup();
    
    for (shared_ptr<mount>& dummy : self->dummyMounts)
    {
        dummy.reset();
    }
    memset(s_usedMountPoints, 0, sizeof(s_usedMountPoints));
}

- (void)testFindEmptyMountPointSlot_AllFull
{
    [self fillAllMountPointSlots];
    
    ssize_t found = FindEmptyMountPointSlot_Locked();
    XCTAssertLessThan(found, 0, "All slots taken, should return error (negative index)");
}

- (void)testFindEmptyMountPointSlot_AtIndex2
{
    constexpr unsigned emptyIndex = 2;
    static_assert(emptyIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    [self fillAllMountPointSlotsExcept:emptyIndex];
    
    ssize_t found = FindEmptyMountPointSlot_Locked();
    XCTAssertEqual(found, emptyIndex);
}

- (void)testFindMountPoint_AllFull
{
    [self fillAllMountPointSlots];
    ssize_t found = FindMountPoint_Locked(self->dummyMounts[1].get());
    XCTAssertEqual(found, 1);
}

- (void)testFindMountPoint_AfterEmpty
{
    constexpr unsigned emptyIndex = 1, lookingForIndex = 3;
    static_assert(emptyIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    static_assert(lookingForIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    static_assert(emptyIndex < lookingForIndex, "");
    
    [self fillAllMountPointSlotsExcept:emptyIndex];

    ssize_t found = FindMountPoint_Locked(self->dummyMounts[lookingForIndex].get());
    XCTAssertEqual(found, lookingForIndex);
}

- (void)testFindMountPoint_NonExisting
{
    constexpr unsigned emptyIndex = 1;
    static_assert(emptyIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    
    [self fillAllMountPointSlotsExcept:emptyIndex];

    shared_ptr<mount> mountNotPresent = mount::Create();
    ssize_t found = FindMountPoint_Locked(mountNotPresent.get());
    XCTAssertLessThan(found, 0);
}

- (void)testFindMountPoint_Empty
{    
    shared_ptr<mount> mountNotPresent = mount::Create();
    ssize_t found = FindMountPoint_Locked(mountNotPresent.get());
    XCTAssertLessThan(found, 0);
}

- (void)testFindOrInsert_InsertEmpty
{
    shared_ptr<mount> mountNotPresent = mount::Create();
    ssize_t inserted = TryFindOrInsertMountPoint_Locked(mountNotPresent.get());
    XCTAssertEqual(inserted, 0);
}

- (void)testFindOrInsert_InsertFull
{
    [self fillAllMountPointSlots];
    shared_ptr<mount> mountNotPresent = mount::Create();
    ssize_t inserted = TryFindOrInsertMountPoint_Locked(mountNotPresent.get());
    XCTAssertLessThan(inserted, 0);
}

- (void)testFindOrInsert_Existing
{
    constexpr unsigned emptyIndex = 1, lookingForIndex = 2;
    static_assert(emptyIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    static_assert(lookingForIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    static_assert(emptyIndex < lookingForIndex, "");
    
    [self fillAllMountPointSlotsExcept:emptyIndex];

    ssize_t found = TryFindOrInsertMountPoint_Locked(self->dummyMounts[lookingForIndex].get());
    XCTAssertEqual(found, lookingForIndex);
}

- (void)testFullSlotsDisableReuse
{
    constexpr unsigned tryReuseIndex = 2;
    static_assert(tryReuseIndex < MaxUsedMountPoints, "If MaxUsedMountPoints changes, this might need updating");
    
    // First, *just* fill the array
    for (unsigned i = 0; i < MaxUsedMountPoints; ++i)
    {
        MountPoint_DisableAuthCache(self->dummyMounts[i].get());
    }

    // Sanity check that the mount we're going to remove is at the expected index
    mount_t tryReuseMount = self->dummyMounts[tryReuseIndex].get();
    ssize_t found = FindMountPoint_Locked(tryReuseMount);
    XCTAssertEqual(found, tryReuseIndex);
    
    // Restoring the cache setting at this point should reuse the slot, as we haven't actually run out yet
    MountPoint_RestoreAuthCache(tryReuseMount);
    found = FindMountPoint_Locked(tryReuseMount);
    XCTAssertLessThan(found, 0);
    
    // Fill all the slots again
    MountPoint_DisableAuthCache(tryReuseMount);

    // Now try disabling the cache on a mount point that won't fit
    shared_ptr<mount> mountNotPresent = mount::Create();
    MountPoint_DisableAuthCache(mountNotPresent.get());
    //XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl, mountNotPresent.get(), 0));

    // If we now restore one of the mounts that have a slot, the slot shouldn't be reused, but the cache on that mount should have been reset
    MountPoint_RestoreAuthCache(tryReuseMount);
    //XCTAssertTrue(MockCalls::DidCallFunction(vfs_clearauthcache_ttl, tryReuseMount));
    found = FindMountPoint_Locked(tryReuseMount);
    XCTAssertEqual(found, tryReuseIndex);
    
    // Disabling the cache again on the one that didn't fit should not cause that mount to get a slot
    MountPoint_DisableAuthCache(mountNotPresent.get());
    found = FindMountPoint_Locked(mountNotPresent.get());
    XCTAssertLessThan(found, 0);
    
    // Restoring cache for a mount not in a slot should be a no-op
    
    MountPoint_RestoreAuthCache(mountNotPresent.get());
    //XCTAssertFalse(MockCalls::DidCallFunction(vfs_clearauthcache_ttl, mountNotPresent.get()));

    // clean up
    for (unsigned i = 0; i < MaxUsedMountPoints; ++i)
    {
        if (i != tryReuseIndex)
        {
            MountPoint_RestoreAuthCache(self->dummyMounts[i].get());
        }
    }
}

@end
