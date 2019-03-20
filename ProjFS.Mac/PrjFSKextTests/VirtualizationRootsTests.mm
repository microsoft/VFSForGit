#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#include "../PrjFSKext/PrjFSProviderUserClient.hpp"
#include "../PrjFSKext/kernel-header-wrappers/mount.h"
#include "../PrjFSKext/Memory.hpp"
#include "../PrjFSKext/Locks.hpp"
#include "../PrjFSKext/VnodeUtilities.hpp"
#include "../PrjFSKext/KextLog.hpp"
#include "../PrjFSKext/public/PrjFSXattrs.h"
#include "KextMockUtilities.hpp"
#include "MockVnodeAndMount.hpp"

#import <XCTest/XCTest.h>
#include <vector>
#include <string>

using std::shared_ptr;
using std::vector;
using KextMock::_;

class PrjFSProviderUserClient
{
};


void ProviderUserClient_UpdatePathProperty(PrjFSProviderUserClient* userClient, const char* providerPath)
{
    MockCalls::RecordFunctionCall(ProviderUserClient_UpdatePathProperty, userClient, providerPath);
}

static void SetRootXattrData(shared_ptr<vnode> vnode)
{
    PrjFSVirtualizationRootXAttrData rootXattr = {};
    vector<uint8_t> rootXattrData(sizeof(rootXattr), 0x00);
    memcpy(rootXattrData.data(), &rootXattr, rootXattrData.size());
    vnode->xattrs.insert(make_pair(PrjFSVirtualizationRootXAttrName, rootXattrData));
}

@interface VirtualizationRootsTests : XCTestCase

@end

@implementation VirtualizationRootsTests
{
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    PerfTracer dummyTracer;
    shared_ptr<mount> testMountPoint;
    vfs_context_t dummyVFSContext;
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
    
    self->dummyVFSContext = vfs_context_create(nullptr);
}

- (void)tearDown
{
    vfs_context_rele(self->dummyVFSContext);
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
    
    shared_ptr<vnode> vnode = self->testMountPoint->CreateVnode(path);
    
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
    vnode->errors.getpath = EINVAL;
    
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

    XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl, _, 0));
    XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty, &self->dummyClient, _));
    
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

- (void)testRegisterProviderForPath_TwoMountPointsInsertionSucceeded
{
    const char* path1 = "/Users/test/code/Repo";
    shared_ptr<vnode> vnode1 = vnode::Create(self->testMountPoint, path1, VDIR);
    
    const char* path2 = "/Volumes/Code/Repo";
    shared_ptr<mount> secondMountPoint = mount::Create();
    shared_ptr<vnode> vnode2 = vnode::Create(secondMountPoint, path2, VDIR);

    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path1);
    XCTAssertEqual(result.error, 0);
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(result.root));

    PrjFSProviderUserClient dummyClient2;
    VirtualizationRootResult result2 = VirtualizationRoot_RegisterProviderForPath(&dummyClient2, 1000, path2);
    XCTAssertEqual(result2.error, 0);
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(result2.root));

    XCTAssertNotEqual(result.root, result2.root);

    if (VirtualizationRoot_IsValidRootHandle(result.root))
    {
        XCTAssertEqual(s_virtualizationRoots[result.root].providerUserClient, &self->dummyClient);

        XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl, self->testMountPoint.get(), _));
        XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty, &self->dummyClient, _));
    
        s_virtualizationRoots[result.root].providerUserClient = nullptr;
        vnode_put(s_virtualizationRoots[result.root].rootVNode);
    }

    if (VirtualizationRoot_IsValidRootHandle(result2.root))
    {
        XCTAssertEqual(s_virtualizationRoots[result2.root].providerUserClient, &dummyClient2);

        XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl, secondMountPoint.get(), 0));
        XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty, &dummyClient2, _));
    
        s_virtualizationRoots[result2.root].providerUserClient = nullptr;
        vnode_put(s_virtualizationRoots[result2.root].rootVNode);
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
    
    shared_ptr<vnode> newVnode = self->testMountPoint->CreateVnode(path, VnodeCreationProperties{ VDIR, oldVnode->GetInode() });
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, 0);
    XCTAssertEqual(result.root, rootIndex);
    XCTAssertEqual(s_virtualizationRoots[result.root].providerUserClient, &self->dummyClient);
    XCTAssertEqual(s_virtualizationRoots[result.root].rootVNode, newVnode.get());
    XCTAssertEqual(s_virtualizationRoots[result.root].rootVNodeVid, newVnode->GetVid());

    XCTAssertTrue(MockCalls::DidCallFunction(vfs_setauthcache_ttl, self->testMountPoint.get(), 0));
    XCTAssertTrue(MockCalls::DidCallFunction(ProviderUserClient_UpdatePathProperty));
    
    s_virtualizationRoots[result.root].providerUserClient = nullptr;
    vnode_put(newVnode.get());
}

- (void)testVnodeIsOnAllowedFilesystem
{
    shared_ptr<mount>  testMountHfs = mount::Create("hfs", fsid_t{}, 0);
    shared_ptr<vnode> testVnodeHfs = vnode::Create(testMountHfs, "/hfs");
    XCTAssertTrue(VirtualizationRoot_VnodeIsOnAllowedFilesystem(testVnodeHfs.get()));

    shared_ptr<mount>  testMountApfs = mount::Create("apfs", fsid_t{}, 0);
    shared_ptr<vnode> testVnodeApfs = vnode::Create(testMountApfs, "/apfs");
    XCTAssertTrue(VirtualizationRoot_VnodeIsOnAllowedFilesystem(testVnodeApfs.get()));

    shared_ptr<mount>  testMountFoo = mount::Create("foo", fsid_t{}, 0);
    shared_ptr<vnode> testVnodeFoo = vnode::Create(testMountFoo, "/foo");
    XCTAssertFalse(VirtualizationRoot_VnodeIsOnAllowedFilesystem(testVnodeFoo.get()));
}

- (void)testIsValidRootHandle
{
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(0));
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(1));
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(2));
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(RootHandle_None));
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(RootHandle_Indeterminate));
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(RootHandle_ProviderTemporaryDirectory));
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(-100));
}

// Check that VirtualizationRoot_FindForVnode correctly identifies the virtualization root for files below that root.
- (void)testFindForVnode_FileInRoot
{
    const char* repoPath = "/Users/test/code/Repo";
    const char* filePath = "/Users/test/code/Repo/file";
    const char* deeplyNestedPath = "/Users/test/code/Repo/deeply/nested/sub/directories/with/a/file";

    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testFileVnode = self->testMountPoint->CreateVnodeTree(filePath);
    shared_ptr<vnode> deepFileVnode = self->testMountPoint->CreateVnodeTree(deeplyNestedPath);
    
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(nullptr /* no client */, 0, repoRootVnode.get(), repoRootVnode->GetVid(), FsidInode{ repoRootVnode->GetMountPoint()->GetFsid(), repoRootVnode->GetInode() }, repoPath);
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testFileVnode.get(), self->dummyVFSContext);
    XCTAssertEqual(foundRoot, repoRootHandle);
    
    foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, deepFileVnode.get(), self->dummyVFSContext);
    XCTAssertEqual(foundRoot, repoRootHandle);
}

// Check that files outside a root are correctly identified as such by VirtualizationRoot_FindForVnode
- (void)testFindForVnode_FileNotInRoot
{
    const char* repoPath = "/Users/test/code/Repo";
    const char* filePath = "/Users/test/code/NotVirtualizedRepo/file";
    
    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testFileVnode = self->testMountPoint->CreateVnodeTree(filePath);
    
    
    VirtualizationRootHandle repoRootHandle = InsertVirtualizationRoot_Locked(nullptr /* no client */, 0, repoRootVnode.get(), repoRootVnode->GetVid(), FsidInode{ repoRootVnode->GetMountPoint()->GetFsid(), repoRootVnode->GetInode() }, repoPath);
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(repoRootHandle));
    
    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testFileVnode.get(), self->dummyVFSContext);
    
    XCTAssertEqual(foundRoot, RootHandle_None);
}

// Check that VirtualizationRoot_FindForVnode can discover virtualization roots from the root directory's xattr
- (void)testFindForVnode_FileInUndetectedRoot
{
    const char* repoPath = "/Users/test/code/Repo";
    const char* filePath = "/Users/test/code/Repo/file";
    
    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testFileVnode = self->testMountPoint->CreateVnodeTree(filePath);

    SetRootXattrData(repoRootVnode);

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testFileVnode.get(), self->dummyVFSContext);
    
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(foundRoot));
}

// A regression test of VirtualizationRoot_FindForVnode for bug #797.
//  * Verify the virtualization root ends up with the inode of the root directory vnode.
//  * Verify that the virtualization root does not change identity when the root directory vnode is recycled.
//  * Verify that the root vnode registered in the virtualization root is refreshed to the new root vnode.
- (void)testFindForVnode_DetectRootRecycleThenFindRootForOtherFile
{
    const char* repoPath = "/Users/test/code/Repo";
    const char* filePath = "/Users/test/code/Repo/file";
    const char* otherFilePath = "/Users/test/code/Repo/some/otherfile";

    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testFileVnode = self->testMountPoint->CreateVnodeTree(filePath);

    SetRootXattrData(repoRootVnode);

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testFileVnode.get(), self->dummyVFSContext);

    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(foundRoot));
    
    if (VirtualizationRoot_IsValidRootHandle(foundRoot))
    {
        uint64_t inode = repoRootVnode->GetInode();
        XCTAssertEqual(s_virtualizationRoots[foundRoot].rootInode, inode);
        
        repoRootVnode->StartRecycling();
        
        shared_ptr<vnode> newRepoRootVnode = self->testMountPoint->CreateVnode(repoPath, VnodeCreationProperties { .inode = repoRootVnode->GetInode(), .type = VDIR });
        
        shared_ptr<vnode> otherTestFileVnode = self->testMountPoint->CreateVnodeTree(otherFilePath);
        VirtualizationRootHandle foundRootForOther = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, otherTestFileVnode.get(), self->dummyVFSContext);
        
        XCTAssertEqual(foundRootForOther, foundRoot, "Despite recycling the repo root vnode, we should end up with the same handle");
        XCTAssertEqual(s_virtualizationRoots[foundRootForOther].rootVNode, newRepoRootVnode.get(), "Vnode should be refreshed");
        XCTAssertEqual(s_virtualizationRoots[foundRootForOther].rootVNodeVid, newRepoRootVnode->GetVid(), "Vnode VID should be refreshed");
        XCTAssertEqual(s_virtualizationRoots[foundRoot].rootInode, inode);
    }
}

// A variation on the above 2 tests but starting with a directory vnode, taking
// the 'if (vnode_isdir(vnode))' branch in VirtualizationRoot_FindForVnode.
- (void)testFindForVnode_DirectoryInUndetectedRoot
{
    const char* repoPath = "/Users/test/code/Repo";
    const char* subdirPath = "/Users/test/code/Repo/some/nested/subdir";
    
    shared_ptr<vnode> repoRootVnode = self->testMountPoint->CreateVnodeTree(repoPath, VDIR);
    shared_ptr<vnode> testSubdirVnode = self->testMountPoint->CreateVnodeTree(subdirPath, VDIR);

    XCTAssertTrue(vnode_isdir(testSubdirVnode.get()));

    SetRootXattrData(repoRootVnode);

    VirtualizationRootHandle foundRoot = VirtualizationRoot_FindForVnode(&self->dummyTracer, PrjFSPerfCounter_VnodeOp_FindRoot, PrjFSPerfCounter_VnodeOp_FindRoot_Iteration, testSubdirVnode.get(), self->dummyVFSContext);
    
    XCTAssertTrue(VirtualizationRoot_IsValidRootHandle(foundRoot));
    if (VirtualizationRoot_IsValidRootHandle(foundRoot))
    {
        XCTAssertEqual(s_virtualizationRoots[foundRoot].rootInode, repoRootVnode->GetInode());
    }
}


@end
