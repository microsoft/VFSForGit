#include "../PrjFSKext/KauthHandlerTestable.hpp"
#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/PrjFSProviderUserClient.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#include "../PrjFSKext/VnodeCachePrivate.hpp"
#include "../PrjFSKext/VnodeCacheTestable.hpp"
#include "../PrjFSKext/PerformanceTracing.hpp"
#include "../PrjFSKext/public/Message.h"
#include "../PrjFSKext/ProviderMessaging.hpp"
#include "../PrjFSKext/public/PrjFSXattrs.h"
#include "../PrjFSKext/kernel-header-wrappers/kauth.h"
#import "KextAssertIntegration.h"
#import <sys/stat.h>
#include "KextMockUtilities.hpp"
#include "MockVnodeAndMount.hpp"
#include "MockProc.hpp"
#include "ProviderMessagingMock.hpp"
#include "VnodeCacheEntriesWrapper.hpp"
#include <tuple>

using std::make_tuple;
using std::shared_ptr;
using std::vector;
using std::string;
using KextMock::_;

class PrjFSProviderUserClient
{
};

@interface HandleFileOpOperationTests : PFSKextTestCase
@end

@implementation HandleFileOpOperationTests
{
    vfs_context_t context;
    const char* repoPath;
    const char* filePath;
    const char* dirPath;
    const char* nonRepoFilePath;
    const char* fromPath;
    const char* fromPathOutOfRepo;
    const char* otherRepoPath;
    const char* fromPathOtherRepo;
    VirtualizationRootHandle repoHandle;
    VirtualizationRootHandle otherRepoHandle;
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    PrjFSProviderUserClient otherDummyClient;
    pid_t otherDummyClientPid;
    shared_ptr<mount> testMount;
    shared_ptr<vnode> repoRootVnode;
    shared_ptr<vnode> testFileVnode;
    shared_ptr<vnode> nonRepoFileVnode;
    shared_ptr<vnode> otherRepoRootVnode;
    shared_ptr<vnode> testDirVnode;
    VnodeCacheEntriesWrapper cacheWrapper;
}

- (void) setUp
{
    [super setUp];

    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
    context = vfs_context_create(NULL);
    dummyClientPid = 100;
    otherDummyClientPid = 200;

    cacheWrapper.AllocateCache();

    // Create Vnode Tree
    repoPath = "/Users/test/code/Repo";
    filePath = "/Users/test/code/Repo/file";
    dirPath = "/Users/test/code/Repo/dir";
    fromPath = "/Users/test/code/Repo/originalfile";
    nonRepoFilePath = "/Users/test/code/NotInRepo/file";
    fromPathOutOfRepo = "/Users/test/code/NotInRepo/fromfile";
    otherRepoPath = "/Users/test/code/OtherRepo";
    fromPathOtherRepo = "/Users/test/code/OtherRepo/fromfile";
    testMount = mount::Create();
    repoRootVnode = testMount->CreateVnodeTree(repoPath, VDIR);
    testFileVnode = testMount->CreateVnodeTree(filePath);
    otherRepoRootVnode = testMount->CreateVnodeTree(otherRepoPath, VDIR);
    nonRepoFileVnode = testMount->CreateVnodeTree(nonRepoFilePath);
    testDirVnode = testMount->CreateVnodeTree(dirPath, VDIR);

    // Register provider for the repository path (Simulate a mount)
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&dummyClient, dummyClientPid, repoPath);
    XCTAssertEqual(result.error, 0);
    self->repoHandle = result.root;
    
    result = VirtualizationRoot_RegisterProviderForPath(&otherDummyClient, otherDummyClientPid, otherRepoPath);
    XCTAssertEqual(result.error, 0);
    self->otherRepoHandle = result.root;

    MockProcess_AddContext(context, 501 /*pid*/);
    MockProcess_SetSelfPid(501);
    MockProcess_AddProcess(501 /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
    
    ProvidermessageMock_ResetResultCount();
    ProviderMessageMock_SetDefaultRequestResult(true);
    ProviderMessageMock_SetSecondRequestResult(true);
}

- (void) tearDownProviders
{
    if (VirtualizationRoot_IsValidRootHandle(self->repoHandle) && VirtualizationRoot_GetActiveProvider(self->repoHandle).isOnline)
    {
        ActiveProvider_Disconnect(self->repoHandle, &dummyClient);
        self->repoHandle = RootHandle_None;
    }

    if (VirtualizationRoot_IsValidRootHandle(self->otherRepoHandle) && VirtualizationRoot_GetActiveProvider(self->otherRepoHandle).isOnline)
    {
        ActiveProvider_Disconnect(self->otherRepoHandle, &otherDummyClient);
        self->otherRepoHandle = RootHandle_None;
    }
}

- (void) tearDown
{
    [self tearDownProviders];
    
    testMount.reset();
    repoRootVnode.reset();
    testFileVnode.reset();
    otherRepoRootVnode.reset();
    nonRepoFileVnode.reset();
    testDirVnode.reset();
    cacheWrapper.FreeCache();
    
    VirtualizationRoots_Cleanup();
    vfs_context_rele(context);
    MockVnodes_CheckAndClear();
    MockCalls::Clear();
    MockProcess_Reset();

    [super tearDown];
}

- (void) testOpen {
    // KAUTH_FILEOP_OPEN should trigger callbacks for files not flagged as in the virtualization root.
    testFileVnode->attrValues.va_flags = 0;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_OPEN,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyFileCreated,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _));
}

- (void) testOpenInVirtualizationRoot {
    // KAUTH_FILEOP_OPEN should not trigger any callbacks for files that already flagged as in the virtualization root.
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_OPEN,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testCloseWithModifed {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_CLOSE,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath),
        KAUTH_FILEOP_CLOSE_MODIFIED,
        0);
    
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyFileModified,
            testFileVnode.get(),
            _,
            filePath,
            _,
            _,
            _,
            _));
}

- (void) testCloseWithModifedWithBitChange {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_CLOSE,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath),
        KAUTH_FILEOP_CLOSE_MODIFIED | 1<<2,
        0);
    
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyFileModified,
            testFileVnode.get(),
            _,
            filePath,
            _,
            _,
            _,
            _));
}

- (void) testCloseWithModifedOnDirectory {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_CLOSE,
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        reinterpret_cast<uintptr_t>(dirPath),
        KAUTH_FILEOP_CLOSE_MODIFIED,
        0);
    
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}


- (void) testCloseWithoutModifed {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_CLOSE,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void)testFileopHardlink
{
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPath),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // from & target repos are the same, should message exactly once
    XCTAssertEqual(1, MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse));
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
}

- (void)testFileopHardlinkOutsideRepo
{
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOutOfRepo),
        reinterpret_cast<uintptr_t>(nonRepoFilePath),
        0,
        0);
    
    // neither from & target are in a root, should not message anyone
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void)testFileopHardlinkIntoRepo
{
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOutOfRepo),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // fromPath is outside repo, filePath is inside, should message exactly once
    XCTAssertEqual(1, MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse));
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
}

- (void)testFileopHardlinkOtherRepo
{
    // Link file from one repo into another
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOtherRepo),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // fromPath is for another repo than filePath, should message both providers
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->otherRepoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
}

- (void)testFileopHardlinkOtherRepoOffline
{
    // Move file from an offline repo into a live one
    ActiveProvider_Disconnect(self->otherRepoHandle, &otherDummyClient);
    
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOtherRepo),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // fromPath is in an offline repo, which can't be messaged
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
    XCTAssertFalse(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->otherRepoHandle,
        _,
        _));
    XCTAssertEqual(1, MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void)testFileopHardlinkOutOfRepo
{
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPath),
        reinterpret_cast<uintptr_t>(nonRepoFilePath),
        0,
        0);
    
    // filePath is outside repo, fromPath is inside, should message exactly once
    XCTAssertEqual(1, MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse));
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
}

- (void)testFileopHardlinkOtherRepoProviderPID
{
    MockProcess_Reset();
    MockProcess_AddContext(context, self->dummyClientPid /*pid*/);
    MockProcess_SetSelfPid(self->dummyClientPid);
    MockProcess_AddProcess(self->dummyClientPid /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "GVFS.Mount" /*name*/);

    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOtherRepo),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // fromPath is for another repo than filePath, but this is the target provider's PID, so only message "from" provider
    XCTAssertFalse(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        _,
        _));
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->otherRepoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
}

- (void)testFileopHardlinkOtherRepoOtherProviderPID
{
    MockProcess_Reset();
    MockProcess_AddContext(context, self->otherDummyClientPid /*pid*/);
    MockProcess_SetSelfPid(self->otherDummyClientPid);
    MockProcess_AddProcess(self->otherDummyClientPid /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "GVFS.Mount" /*name*/);

    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleFileOpOperation(
        nullptr,
        nullptr,
        KAUTH_FILEOP_LINK,
        reinterpret_cast<uintptr_t>(fromPathOtherRepo),
        reinterpret_cast<uintptr_t>(filePath),
        0,
        0);
    
    // fromPath is for another repo than filePath, but this is the "from" provider's PID, so only message target provider
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->repoHandle,
        MessageType_KtoU_NotifyFileHardLinkCreated,
        _));
    XCTAssertFalse(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        self->otherRepoHandle,
        _,
        _));
}

- (void)testNoPendingRenameRecordedOnIneligibleFilesystem
{
    shared_ptr<mount> testMountNone = mount::Create("msdos", fsid_t{}, 0);
    const string testMountPath = "/Volumes/USBSTICK";
    shared_ptr<vnode> testMountRoot = testMountNone->CreateVnodeTree(testMountPath, VDIR);
    const string filePath = testMountPath + "/file";
    shared_ptr<vnode> testVnode = testMountNone->CreateVnodeTree(filePath);
    const string renamedFilePath = filePath + "_renamed";

    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(testVnode.get()),
        reinterpret_cast<uintptr_t>(filePath.c_str()),
        reinterpret_cast<uintptr_t>(renamedFilePath.c_str()),
        0); // unused
    XCTAssertEqual(0, s_pendingRenameCount);
}

@end
