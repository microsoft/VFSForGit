#import <XCTest/XCTest.h>
#include "MockVnodeAndMount.hpp"
#include "KextLogMock.h"
#include "KextMockUtilities.hpp"
#include "VnodeCacheEntriesWrapper.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#include "../PrjFSKext/VnodeCache.hpp"
#include "../PrjFSKext/VnodeCachePrivate.hpp"
#include "../PrjFSKext/VnodeCacheTestable.hpp"

using KextMock::_;
using std::shared_ptr;

@interface VnodeCacheTests : XCTestCase
@end

@implementation VnodeCacheTests
{
    std::string repoPath;
    shared_ptr<mount> testMount;
    shared_ptr<vnode> repoRootVnode;
    shared_ptr<vnode> testVnodeFile1;
    shared_ptr<vnode> testVnodeFile2;
    shared_ptr<vnode> testVnodeFile3;
    PerfTracer dummyPerfTracer;
    vfs_context_t dummyVFSContext;
    VnodeCacheEntriesWrapper cacheWrapper;
}

static const VirtualizationRootHandle DummyRootHandle = 51;
static const VirtualizationRootHandle DummyRootHandleTwo = 52;

- (void)setUp
{
    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
    
    self->testMount = mount::Create();
    self->repoPath = "/Users/test/code/Repo";
    self->repoRootVnode = self->testMount->CreateVnodeTree(repoPath, VDIR);
    self->testVnodeFile1 = testMount->CreateVnodeTree(repoPath + "/file1");
    self->testVnodeFile2 = testMount->CreateVnodeTree(repoPath + "/file2");
    self->testVnodeFile3 = testMount->CreateVnodeTree(repoPath + "/file3");
    self->dummyVFSContext = vfs_context_create(nullptr);
    self->cacheWrapper.AllocateCache();
    InitCacheStats();
}

- (void)tearDown
{
    self->cacheWrapper.FreeCache();
    vfs_context_rele(self->dummyVFSContext);
    self->testVnodeFile3.reset();
    self->testVnodeFile2.reset();
    self->testVnodeFile1.reset();
    self->repoRootVnode.reset();
    self->testMount.reset();
    MockCalls::Clear();
    VirtualizationRoots_Cleanup();
}

- (void)testInitCacheStats {
    // We need to validate that InitCacheStats sets all of the cache health stats to zero, so
    // first set them all to something non-zero
    atomic_exchange(&s_cacheStats.cacheEntries, 1U);
    
    for (int32_t i = 0; i < VnodeCacheHealthStat_Count; ++i)
    {
        atomic_exchange(&s_cacheStats.healthStats[i], 1ULL);
    }
    
    InitCacheStats();

    XCTAssertTrue(s_cacheStats.cacheEntries == 0);
    
    for (int32_t i = 0; i < VnodeCacheHealthStat_Count; ++i)
    {
        XCTAssertTrue(s_cacheStats.healthStats[i] == 0);
    }
}

- (void)testAtomicFetchAddCacheHealthStat {
    for (int32_t i = 0; i < VnodeCacheHealthStat_Count; ++i)
    {
        AtomicFetchAddCacheHealthStat(static_cast<VnodeCacheHealthStat>(i), 1ULL);
        XCTAssertTrue(s_cacheStats.healthStats[i] == 1);
    }
    
    for (int32_t i = 0; i < VnodeCacheHealthStat_Count; ++i)
    {
        AtomicFetchAddCacheHealthStat(static_cast<VnodeCacheHealthStat>(i), 2ULL);
        XCTAssertTrue(s_cacheStats.healthStats[i] == 3);
    }
    
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
    
    atomic_exchange(&s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount], UINT64_MAX - 1000);
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_InvalidateEntireCacheCount, 1ULL);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount] == UINT64_MAX - 999);
    AtomicFetchAddCacheHealthStat(VnodeCacheHealthStat_InvalidateEntireCacheCount, 1ULL);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount] == 0ULL);
    
    XCTAssertTrue(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_DEFAULT));
}

- (void)testVnodeCache_FindRootForVnode_EmptyCache {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));
    
    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();
    
    XCTAssertTrue(repoRootHandle == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 0);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalCacheLookups] == 2);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == 0);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testVnodeCache_FindRootForVnode_FullCache {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));
    
    self->cacheWrapper.FillAllEntries();
    
    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();
    
    XCTAssertTrue(repoRootHandle == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 0);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 1);
    
    // 3 -> The initial lookup, the attempt to update the full cache, the lookup after emptying the cache
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalCacheLookups] == 3);
    
    // Capacity * 2 -> The first two lookups (before invalidating the cache), will collide with every entry
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == self->cacheWrapper.GetCapacity() * 2);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testVnodeCache_FindRootForVnode_VnodeInCache {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));
    
    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();
    
    // The first call to VnodeCache_FindRootForVnode results in the vnode being added to the cache
    XCTAssertTrue(repoRootHandle == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 0);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 1);
    
    uintptr_t vnodeIndex = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[vnodeIndex].vnode);
    XCTAssertTrue(self->testVnodeFile1->GetVid() == self->cacheWrapper[vnodeIndex].vid);
    XCTAssertTrue(repoRootHandle == self->cacheWrapper[vnodeIndex].virtualizationRoot);
    
    XCTAssertTrue(repoRootHandle == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 1);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testVnodeCache_RefreshRootForVnode {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertEqual(foundRoot, repoRootHandle);

    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();

    // Make sure the cache is empty to start
    InvalidateCache_ExclusiveLocked();
    
    // Insert testFileVnode with DummyRootHandle as its root
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    // VnodeCache_RefreshRootForVnode should
    // force a lookup of the new root and set it in the cache
    VirtualizationRootHandle rootHandle = VnodeCache_RefreshRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertTrue(rootHandle == repoRootHandle);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(rootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalRefreshRootForVnode] == 1);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testVnodeCache_InvalidateVnodeRootAndGetLatestRoot {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertEqual(foundRoot, repoRootHandle);

    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();

    // Make sure the cache is empty to start
    InvalidateCache_ExclusiveLocked();
    
    // Insert testFileVnode with DummyRootHandle as its root
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    // VnodeCache_InvalidateVnodeRootAndGetLatestRoot should return the real root and
    // set the entry in the cache to RootHandle_Indeterminate
    VirtualizationRootHandle rootHandle = VnodeCache_InvalidateVnodeRootAndGetLatestRoot(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertTrue(rootHandle == repoRootHandle);
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(RootHandle_Indeterminate == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalInvalidateVnodeRoot] == 1);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testVnodeCache_InvalidateCache_SetsMemoryToZeros {
    self->cacheWrapper.FillAllEntries();

    shared_ptr<VnodeCacheEntry> emptyArray(static_cast<VnodeCacheEntry*>(calloc(self->cacheWrapper.GetCapacity(), sizeof(VnodeCacheEntry))), free);
    XCTAssertTrue(0 != memcmp(emptyArray.get(), s_entries, sizeof(VnodeCacheEntry) * self->cacheWrapper.GetCapacity()));
    
    VnodeCache_InvalidateCache(&self->dummyPerfTracer);
    XCTAssertTrue(0 == memcmp(emptyArray.get(), s_entries, sizeof(VnodeCacheEntry) * self->cacheWrapper.GetCapacity()));
    
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount] == 1);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testInvalidateCache_ExclusiveLocked_SetsMemoryToZeros {
    self->cacheWrapper.FillAllEntries();
    
    shared_ptr<VnodeCacheEntry> emptyArray(static_cast<VnodeCacheEntry*>(calloc(self->cacheWrapper.GetCapacity(), sizeof(VnodeCacheEntry))), free);
    XCTAssertTrue(0 != memcmp(emptyArray.get(), s_entries, sizeof(VnodeCacheEntry) * self->cacheWrapper.GetCapacity()));
    
    InvalidateCache_ExclusiveLocked();
    XCTAssertTrue(0 == memcmp(emptyArray.get(), s_entries, sizeof(VnodeCacheEntry) * self->cacheWrapper.GetCapacity()));
    
    // VnodeCacheHealthStat_InvalidateEntireCacheCount is adjusted by VnodeCache_InvalidateCache
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_InvalidateEntireCacheCount] == 0);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testComputePow2CacheCapacity {

    // At a minimum ComputePow2CacheCapacity should return the minimum value in AllowedPow2CacheCapacities
    XCTAssertTrue(MinPow2VnodeCacheCapacity == ComputePow2CacheCapacity(0));
    
    // ComputePow2CacheCapacity should round up to the nearest power of 2 (after multiplying expectedVnodeCount by 2)
    int expectedVnodeCount = MinPow2VnodeCacheCapacity/2 + 1;
    XCTAssertTrue(MinPow2VnodeCacheCapacity << 1 == ComputePow2CacheCapacity(expectedVnodeCount));
    
    // ComputePow2CacheCapacity should be capped at the maximum value in AllowedPow2CacheCapacities
    XCTAssertTrue(MaxPow2VnodeCacheCapacity == ComputePow2CacheCapacity(MaxPow2VnodeCacheCapacity + 1));
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryGetVnodeRootFromCache_VnodeInCache {
    uintptr_t testIndex = 5;
    self->cacheWrapper[testIndex].vnode = self->testVnodeFile1.get();
    self->cacheWrapper[testIndex].vid = self->testVnodeFile1->GetVid();
    self->cacheWrapper[testIndex].virtualizationRoot = DummyRootHandle;
    
    VirtualizationRootHandle rootHandle = 1;
    XCTAssertTrue(
        TryGetVnodeRootFromCache(
            self->testVnodeFile1.get(),
            testIndex,
            self->testVnodeFile1->GetVid(),
            rootHandle));
    XCTAssertTrue(DummyRootHandle == rootHandle);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryGetVnodeRootFromCache_VnodeNotInCache {
    VirtualizationRootHandle rootHandle = 1;
    XCTAssertFalse(
        TryGetVnodeRootFromCache(
            self->testVnodeFile1.get(),
            ComputeVnodeHashIndex(self->testVnodeFile1.get()),
            self->testVnodeFile1->GetVid(),
            rootHandle));
    XCTAssertTrue(RootHandle_None == rootHandle);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testInsertEntryToInvalidatedCache_ExclusiveLocked_CacheFull {
    // In production InsertEntryToInvalidatedCache_ExclusiveLocked should never
    // be called when the cache is full, but by this test doing so we can validate
    // that InsertEntryToInvalidatedCache_ExclusiveLocked logs an error if it fails to
    // insert a vnode into the cache
    self->cacheWrapper.FillAllEntries();
    
    InsertEntryToInvalidatedCache_ExclusiveLocked(
        self->testVnodeFile1.get(),
        ComputeVnodeHashIndex(self->testVnodeFile1.get()),
        self->testVnodeFile1->GetVid(),
        DummyRootHandle);
    
    XCTAssertTrue(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_ERROR));
}

- (void)testFindVnodeRootFromDiskAndUpdateCache_RefreshAndInvalidateEntry {
    VirtualizationRootHandle onDiskRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(onDiskRootHandle));
    XCTAssertTrue(DummyRootHandle != onDiskRootHandle);

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertEqual(foundRoot, onDiskRootHandle);

    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();

    // Make sure the cache is empty
    InvalidateCache_ExclusiveLocked();
    
    // Insert testFileVnode with DummyRootHandle as its root
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    // FindVnodeRootFromDiskAndUpdateCache with UpdateCacheBehavior_ForceRefresh should
    // force a lookup of the new root and set it in the cache
    VirtualizationRootHandle rootHandle;
    FindVnodeRootFromDiskAndUpdateCache(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        self->dummyVFSContext,
        self->testVnodeFile1.get(),
        indexFromHash,
        testVnodeVid,
        UpdateCacheBehavior_ForceRefresh,
        /* out parameters */
        rootHandle);
    XCTAssertTrue(rootHandle == onDiskRootHandle);
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(rootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);

    // UpdateCacheBehavior_InvalidateEntry means that the root in the cache should be
    // set to RootHandle_Indeterminate, but the real root will still be returned
    FindVnodeRootFromDiskAndUpdateCache(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        self->dummyVFSContext,
        self->testVnodeFile1.get(),
        indexFromHash,
        testVnodeVid,
        UpdateCacheBehavior_InvalidateEntry,
        /* out parameters */
        rootHandle);
    XCTAssertTrue(rootHandle == onDiskRootHandle);
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(RootHandle_Indeterminate == self->cacheWrapper[indexFromHash].virtualizationRoot);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testFindVnodeRootFromDiskAndUpdateCache_FullCache {
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(
        nullptr /* no client */,
        0,
        self->repoRootVnode.get(),
        self->repoRootVnode->GetVid(),
        FsidInode{ self->repoRootVnode->GetMountPoint()->GetFsid(), self->repoRootVnode->GetInode() },
        self->repoPath.c_str());
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext);
    XCTAssertEqual(foundRoot, repoRootHandle);

    // We don't care which mocks were called during the initialization code (above)
    MockCalls::Clear();

    // Fill every entry in the cache
    self->cacheWrapper.FillAllEntries();
    
    // Insert testFileVnode with DummyRootHandle as its root
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();
    
    // UpdateCacheBehavior_TrustCurrentEntry will use the current entry if present
    // In this case there is no entry for the vnode and so the cache will be invalidated
    // and a new entry added
    VirtualizationRootHandle rootHandle;
    FindVnodeRootFromDiskAndUpdateCache(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        self->dummyVFSContext,
        self->testVnodeFile1.get(),
        indexFromHash,
        testVnodeVid,
        UpdateCacheBehavior_TrustCurrentEntry,
        /* out parameters */
        rootHandle);
    XCTAssertTrue(rootHandle == repoRootHandle);
    
    for (uintptr_t index = 0; index < self->cacheWrapper.GetCapacity(); ++index)
    {
        if (index == indexFromHash)
        {
            XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
            XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
            XCTAssertTrue(rootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
        }
        else
        {
            XCTAssertTrue(nullptr == self->cacheWrapper[index].vnode);
            XCTAssertTrue(0 == self->cacheWrapper[index].vid);
            XCTAssertTrue(0 == self->cacheWrapper[index].virtualizationRoot);
        }
    }
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testFindVnodeRootFromDiskAndUpdateCache_InvalidUpdateCacheBehaviorValue {
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();
    
    // This test is to ensure 100% coverage of FindVnodeRootFromDiskAndUpdateCache
    // In production FindVnodeRootFromDiskAndUpdateCache would panic when it sees an
    // invalid UpdateCacheBehavior, but in user-mode the assertf if a no-op
    VirtualizationRootHandle rootHandle;
    FindVnodeRootFromDiskAndUpdateCache(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        self->dummyVFSContext,
        self->testVnodeFile1.get(),
        indexFromHash,
        testVnodeVid,
        UpdateCacheBehavior_Invalid,
        /* out parameters */
        rootHandle);
}

- (void)testTryFindVnodeIndex_Locked_ReturnsVnodeHashIndexWhenSlotEmpty {
    uintptr_t vnodeHashIndex = 5;
    uintptr_t cacheIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(self->testVnodeFile1.get(), vnodeHashIndex, /* out */ cacheIndex));
    XCTAssertTrue(cacheIndex == vnodeHashIndex);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == 0);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryFindVnodeIndex_Locked_ReturnsFalseWhenCacheFull {
    self->cacheWrapper.FillAllEntries();
    uintptr_t vnodeIndex;
    XCTAssertFalse(
        TryFindVnodeIndex_Locked(
            self->testVnodeFile1.get(),
            ComputeVnodeHashIndex(self->testVnodeFile1.get()),
            /* out */ vnodeIndex));
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == self->cacheWrapper.GetCapacity());
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryFindVnodeIndex_Locked_WrapsToBeginningWhenResolvingCollisions {
    self->cacheWrapper.FillAllEntries();
    uintptr_t emptyIndex = 2;
    cacheWrapper.MarkEntryAsFree(emptyIndex);
    
    uintptr_t vnodeHashIndex = 5;
    uintptr_t vnodeIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(self->testVnodeFile1.get(), vnodeHashIndex, /* out */ vnodeIndex));
    XCTAssertTrue(emptyIndex == vnodeIndex);
    
    uint64_t expectedCollisions = (self->cacheWrapper.GetCapacity() - vnodeHashIndex) + emptyIndex;
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == expectedCollisions);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryFindVnodeIndex_Locked_ReturnsLastIndexWhenEmptyAndResolvingCollisions {
    self->cacheWrapper.FillAllEntries();
    uintptr_t emptyIndex = cacheWrapper.GetCapacity() - 1;
    cacheWrapper.MarkEntryAsFree(emptyIndex);
    
    uintptr_t vnodeHashIndex = 5;
    uintptr_t vnodeIndex;
    XCTAssertTrue(TryFindVnodeIndex_Locked(self->testVnodeFile1.get(), vnodeHashIndex, /* out */ vnodeIndex));
    XCTAssertTrue(emptyIndex == vnodeIndex);

    uint64_t expectedCollisions = (self->cacheWrapper.GetCapacity() - 1) - vnodeHashIndex;
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == expectedCollisions);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryInsertOrUpdateEntry_ExclusiveLocked_ReturnsFalseWhenFull {
    self->cacheWrapper.FillAllEntries();

    XCTAssertFalse(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            ComputeVnodeHashIndex(self->testVnodeFile1.get()),
            self->testVnodeFile1->GetVid(),
            true, // forceRefreshEntry
            DummyRootHandle));
    
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalLookupCollisions] == self->cacheWrapper.GetCapacity());
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryInsertOrUpdateEntry_ExclusiveLocked_ReplacesIndeterminateEntry {
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    uint32_t testVnodeVid = self->testVnodeFile1->GetVid();

    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            true, // forceRefreshEntry
            RootHandle_Indeterminate));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(RootHandle_Indeterminate == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            testVnodeVid,
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(testVnodeVid == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(DummyRootHandle == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 0);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryInsertOrUpdateEntry_ExclusiveLocked_ReplacesEntryAfterRecyclingVnode {
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());
    
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            self->testVnodeFile1->GetVid(),
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(self->testVnodeFile1->GetVid() == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    self->testVnodeFile1->StartRecycling();
    
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            self->testVnodeFile1->GetVid(),
            false, // forceRefreshEntry
            DummyRootHandleTwo));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(self->testVnodeFile1->GetVid() == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandleTwo == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(DummyRootHandleTwo == VnodeCache_FindRootForVnode(
        &self->dummyPerfTracer,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
        PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        self->testVnodeFile1.get(),
        self->dummyVFSContext));
    
    XCTAssertTrue(s_cacheStats.cacheEntries == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeHits] == 1);
    XCTAssertTrue(s_cacheStats.healthStats[VnodeCacheHealthStat_TotalFindRootForVnodeMisses] == 0);
    
    // Sanity check: We don't expect any of the mock functions to have been called
    XCTAssertFalse(MockCalls::DidCallAnyFunctions());
}

- (void)testTryInsertOrUpdateEntry_ExclusiveLocked_LogsErrorWhenCacheHasDifferentRoot {
    uintptr_t indexFromHash = ComputeVnodeHashIndex(self->testVnodeFile1.get());

    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            self->testVnodeFile1->GetVid(),
            false, // forceRefreshEntry
            DummyRootHandle));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(self->testVnodeFile1->GetVid() == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(
        TryInsertOrUpdateEntry_ExclusiveLocked(
            self->testVnodeFile1.get(),
            indexFromHash,
            self->testVnodeFile1->GetVid(),
            false, // forceRefreshEntry
            DummyRootHandleTwo));
    XCTAssertTrue(self->testVnodeFile1.get() == self->cacheWrapper[indexFromHash].vnode);
    XCTAssertTrue(self->testVnodeFile1->GetVid() == self->cacheWrapper[indexFromHash].vid);
    XCTAssertTrue(DummyRootHandle == self->cacheWrapper[indexFromHash].virtualizationRoot);
    
    XCTAssertTrue(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_ERROR));
}

@end
