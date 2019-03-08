#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#include "../PrjFSKext/PrjFSProviderUserClient.hpp"
#include "../PrjFSKext/kernel-header-wrappers/mount.h"
#include "../PrjFSKext/Memory.hpp"
#include "../PrjFSKext/Locks.hpp"
#include "../PrjFSKext/VnodeUtilities.hpp"
#include "../PrjFSKext/KextLog.hpp"
#include "KextMockUtilities.hpp"
#include "MockVnodeAndMount.hpp"

#import <XCTest/XCTest.h>
#include <vector>
#include <string>

using std::shared_ptr;

class PrjFSProviderUserClient
{
};


void ProviderUserClient_UpdatePathProperty(PrjFSProviderUserClient* userClient, const char* providerPath)
{
    MockCalls::RecordFunctionCall(ProviderUserClient_UpdatePathProperty, userClient, providerPath);
}


@interface VirtualizationRootsTests : XCTestCase

@end

@implementation VirtualizationRootsTests
{
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    PerfTracer dummyTracer;
    shared_ptr<mount> testMountPoint;
}

- (void)setUp
{
    srand(0);
    self->dummyClientPid = 100;
    
    // This is roughly what "real" fsids look like
    fsid_t testMountFsid = { (1 << 24) | (rand() % 32), (rand() % 16) };
    
    // HFS+ inodes are < UINT32_MAX (APFS' are 64-bit)
    uint64_t testMountInitialInode = rand();
    
    testMountPoint = mount::Create("hfs", testMountFsid, testMountInitialInode);
    
    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
}

- (void)tearDown
{
    VirtualizationRoots_Cleanup();

    MockVnodes_CheckAndClear();
    MockCalls::Clear();
}


- (void)testRegisterProviderForPath_NonexistentPath
{
    const char* path = "/Users/test/code/Does/Not/Exist";
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENOENT);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));
    
    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
}

- (void)testRegisterProviderForPath_NonDirectoryPath
{
    const char* path = "/Users/test/code/NotADirectory.cpp";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path);
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENOTDIR);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));

    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
}

- (void)testRegisterProviderForPath_DisallowedFileSystem
{
    const char* path = "/Volumes/USBStick/repo";
    
    fsid_t fatFsid = self->testMountPoint->GetFsid();
    fatFsid.val[1]++;
    shared_ptr<mount> fatMount(mount::Create("msdos", fatFsid, rand()));
    
    shared_ptr<vnode> vnode = vnode::Create(fatMount, path, VDIR);
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENODEV);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));

    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
}

- (void)testRegisterProviderForPath_GetPathError
{
    const char* path = "/Users/test/code/RepoNotInNamecache";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path, VDIR);
    vnode->SetGetPathError(EINVAL);
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, EINVAL);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));

    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
}

- (void)testRegisterProviderForPath_ExistingRoot
{
    const char* path = "/Users/test/code/Repo";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path, VDIR);
    
    const VirtualizationRootHandle rootIndex = 2;
    XCTAssertLessThan(rootIndex, s_maxVirtualizationRoots);
    
    s_virtualizationRoots[rootIndex].inUse = true;
    s_virtualizationRoots[rootIndex].rootVNode = vnode.get();
    s_virtualizationRoots[rootIndex].rootVNodeVid = vnode_vid(vnode.get());
    s_virtualizationRoots[rootIndex].rootFsid = self->testMountPoint->GetFsid();
    s_virtualizationRoots[rootIndex].rootInode = vnode->GetInode();
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, 0);
    XCTAssertEqual(result.root, rootIndex);
    XCTAssertEqual(s_virtualizationRoots[result.root].providerUserClient, &self->dummyClient);

    XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
    XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
    
    s_virtualizationRoots[result.root].providerUserClient = nullptr;
    vnode_put(s_virtualizationRoots[result.root].rootVNode);

}

- (void)testRegisterProviderForPath_ProviderExists
{
    const char* path = "/Users/test/code/Repo";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path, VDIR);
    
    const VirtualizationRootHandle rootIndex = 1;
    XCTAssertLessThan(rootIndex, s_maxVirtualizationRoots);
    
    PrjFSProviderUserClient existingClient;
    const pid_t existingClientPid = 50;

    
    s_virtualizationRoots[rootIndex].inUse = true;
    s_virtualizationRoots[rootIndex].rootVNode = vnode.get();
    s_virtualizationRoots[rootIndex].rootVNodeVid = vnode->GetVid();
    s_virtualizationRoots[rootIndex].rootFsid = self->testMountPoint->GetFsid();
    s_virtualizationRoots[rootIndex].rootInode = vnode->GetInode();
    
    vnode_get(s_virtualizationRoots[rootIndex].rootVNode);
    
    s_virtualizationRoots[rootIndex].providerUserClient = &existingClient;
    s_virtualizationRoots[rootIndex].providerPid = existingClientPid;
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, EBUSY);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));
    XCTAssertNotEqual(s_virtualizationRoots[rootIndex].providerUserClient, &self->dummyClient);

    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
    
    s_virtualizationRoots[rootIndex].providerUserClient = nullptr;
    vnode_put(s_virtualizationRoots[rootIndex].rootVNode);
}


- (void)testRegisterProviderForPath_InsertionSucceeded
{
    const char* path = "/Users/test/code/Repo";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path, VDIR);
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, 0);
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(result.root));
    if (VirtualizationRoot_IsValidRootHandle(result.root))
    {
        XCTAssertEqual(s_virtualizationRoots[result.root].providerUserClient, &self->dummyClient);

        XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
        XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
    
        s_virtualizationRoots[result.root].providerUserClient = nullptr;
        vnode_put(s_virtualizationRoots[result.root].rootVNode);
    }
}

- (void)testRegisterProviderForPath_ArrayFull
{
    const char* path = "/Users/test/code/Repo";
    
    shared_ptr<vnode> vnode = vnode::Create(self->testMountPoint, path, VDIR);
    
    Memory_FreeArray(s_virtualizationRoots, s_maxVirtualizationRoots);
    s_maxVirtualizationRoots = INT16_MAX + 1;
    s_virtualizationRoots = Memory_AllocArray<VirtualizationRoot>(INT16_MAX + 1);
    memset(s_virtualizationRoots, 0, s_maxVirtualizationRoots * sizeof(s_virtualizationRoots[0]));
    
    for (uint32_t i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        s_virtualizationRoots[i].inUse = true;
    }
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENOMEM);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));

    XCTAssertFalse(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
}


- (void)testRegisterProviderForPath_ExistingRecycledRoot
{
    const char* path = "/Users/test/code/Repo";
    
    shared_ptr<vnode> oldVnode = vnode::Create(self->testMountPoint, path, VDIR);
    
    const VirtualizationRootHandle rootIndex = 2;
    XCTAssertLessThan(rootIndex, s_maxVirtualizationRoots);
    
    s_virtualizationRoots[rootIndex].inUse = true;
    s_virtualizationRoots[rootIndex].rootVNode = oldVnode.get();
    s_virtualizationRoots[rootIndex].rootVNodeVid = oldVnode->GetVid();
    s_virtualizationRoots[rootIndex].rootFsid = self->testMountPoint->GetFsid();
    uint64_t inode = oldVnode->GetInode();
    s_virtualizationRoots[rootIndex].rootInode = inode;
    
    oldVnode->StartRecycling();
    
    shared_ptr<vnode> newVnode = vnode::Create(self->testMountPoint, path, VDIR, oldVnode->GetInode());
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, 0);
    XCTAssertEqual(result.root, rootIndex);
    XCTAssertEqual(s_virtualizationRoots[result.root].providerUserClient, &self->dummyClient);
    XCTAssertEqual(s_virtualizationRoots[result.root].rootVNode, newVnode.get());
    XCTAssertEqual(s_virtualizationRoots[result.root].rootVNodeVid, newVnode->GetVid());

    XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl));
    XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
    
    s_virtualizationRoots[result.root].providerUserClient = nullptr;
    vnode_put(newVnode.get());

}

// Check that VirtualizationRoot_FindForVnode correctly identifies the virtualization root for files below that root.
- (void)testFindForVnode_FileInRepo
{
    vfs_context_t context = vfs_context_create(nullptr);
    
    const char* repoPath = "/Users/test/code/Repo";
    const char* filePath = "/Users/test/code/Repo/file";
    
    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testFileVnode = self->testMountPoint->CreateVnodeTree(filePath);
    
    
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(nullptr /* no client */, 0, repoRootVnode.get(), repoRootVnode->GetVid(), FsidInode{ repoRootVnode->GetMountPoint()->GetFsid(), repoRootVnode->GetInode() }, repoPath);
    
    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testFileVnode.get(), context);
    
    XCTAssertEqual(foundRoot, repoRootHandle);
    vfs_context_rele(context);
}

@end
