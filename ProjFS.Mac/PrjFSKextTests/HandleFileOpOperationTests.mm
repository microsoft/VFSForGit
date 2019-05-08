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
#import <XCTest/XCTest.h>
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
using KextMock::_;

class PrjFSProviderUserClient
{
};

@interface HandleFileOpOperationTests : XCTestCase
@end

@implementation HandleFileOpOperationTests
{
    vfs_context_t context;
    const char* repoPath;
    const char* filePath;
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
    VnodeCacheEntriesWrapper cacheWrapper;
}

- (void) setUp
{
    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
    context = vfs_context_create(NULL);
    dummyClientPid = 100;
    otherDummyClientPid = 200;

    cacheWrapper.AllocateCache();

    // Create Vnode Tree
    repoPath = "/Users/test/code/Repo";
    filePath = "/Users/test/code/Repo/file";
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
    ProviderMessageMock_SetCleanupRootsAfterRequest(false);
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
    cacheWrapper.FreeCache();
    
    VirtualizationRoots_Cleanup();
    vfs_context_rele(context);
    MockVnodes_CheckAndClear();
    MockCalls::Clear();
    MockProcess_Reset();
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

@end
