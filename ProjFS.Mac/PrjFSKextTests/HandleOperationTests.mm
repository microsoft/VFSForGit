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
using KextMock::_;

class PrjFSProviderUserClient
{
};

static void SetPrjFSFileXattrData(const shared_ptr<vnode>& vnode)
{
    PrjFSFileXAttrData rootXattr = {};
    vector<uint8_t> rootXattrData(sizeof(rootXattr), 0x00);
    memcpy(rootXattrData.data(), &rootXattr, rootXattrData.size());
    vnode->xattrs.insert(make_pair(PrjFSFileXAttrName, rootXattrData));
}

@interface HandleVnodeOperationTests : PFSKextTestCase
@end

@implementation HandleVnodeOperationTests
{
    vfs_context_t context;
    const char* repoPath;
    const char* filePath;
    const char* dirPath;
    VirtualizationRootHandle dummyRepoHandle;
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    shared_ptr<mount> testMount;
    shared_ptr<vnode> repoRootVnode;
    shared_ptr<vnode> testFileVnode;
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

    cacheWrapper.AllocateCache();

    // Create Vnode Tree
    repoPath = "/Users/test/code/Repo";
    filePath = "/Users/test/code/Repo/file";
    dirPath = "/Users/test/code/Repo/dir";
    testMount = mount::Create();
    repoRootVnode = testMount->CreateVnodeTree(repoPath, VDIR);
    testFileVnode = testMount->CreateVnodeTree(filePath);
    testDirVnode = testMount->CreateVnodeTree(dirPath, VDIR);

    // Register provider for the repository path (Simulate a mount)
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&dummyClient, dummyClientPid, repoPath);
    XCTAssertEqual(result.error, 0);
    self->dummyRepoHandle = result.root;

    MockProcess_AddContext(context, 501 /*pid*/);
    MockProcess_SetSelfPid(501);
    MockProcess_AddProcess(501 /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
    
    ProvidermessageMock_ResetResultCount();
    ProviderMessageMock_SetDefaultRequestResult(true);
    ProviderMessageMock_SetSecondRequestResult(true);
}

- (void) tearDown
{
    if (VirtualizationRoot_GetActiveProvider(self->dummyRepoHandle).isOnline)
    {
        ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
    }

    testMount.reset();
    repoRootVnode.reset();
    testFileVnode.reset();
    testDirVnode.reset();
    cacheWrapper.FreeCache();
    
    VirtualizationRoots_Cleanup();
    vfs_context_rele(context);
    MockVnodes_CheckAndClear();
    MockCalls::Clear();
    MockProcess_Reset();

    [super tearDown];
}

- (void) removeAllVirtualizationRoots
{
    if (VirtualizationRoot_GetActiveProvider(self->dummyRepoHandle).isOnline)
    {
        ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
    }
    
    VirtualizationRoots_Cleanup();
    VirtualizationRoots_Init();
}

- (void) testEmptyFileHydrates {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    
    const int actionCount = 9;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_WRITE_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES,
        KAUTH_VNODE_READ_DATA,
        KAUTH_VNODE_WRITE_DATA,
        KAUTH_VNODE_EXECUTE,
        KAUTH_VNODE_DELETE,
        KAUTH_VNODE_APPEND_DATA,
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertTrue(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                testFileVnode.get(),
                _,
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testVnodeAccessCausesNoEvent {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    
    const int actionCount = 9;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_READ_ATTRIBUTES | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_WRITE_ATTRIBUTES | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_READ_EXTATTRIBUTES | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_READ_DATA | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_WRITE_DATA | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_EXECUTE | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_DELETE | KAUTH_VNODE_ACCESS,
        KAUTH_VNODE_APPEND_DATA | KAUTH_VNODE_ACCESS,
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
        MockCalls::Clear();
    }
}


- (void) testNonEmptyFileDoesNotHydrate {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    
    const int actionCount = 9;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_WRITE_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES,
        KAUTH_VNODE_READ_DATA,
        KAUTH_VNODE_WRITE_DATA,
        KAUTH_VNODE_EXECUTE,
        KAUTH_VNODE_DELETE,
        KAUTH_VNODE_APPEND_DATA,
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                testFileVnode.get(),
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testNonEmptyFileWithPrjFSFileXAttrNameDoesNotHydrate {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    
    const int actionCount = 9;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_WRITE_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES,
        KAUTH_VNODE_READ_DATA,
        KAUTH_VNODE_WRITE_DATA,
        KAUTH_VNODE_EXECUTE,
        KAUTH_VNODE_DELETE,
        KAUTH_VNODE_APPEND_DATA,
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                testFileVnode.get(),
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testEventsThatShouldNotHydrate {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    
    const int actionCount = 4;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_DELETE_CHILD,
        KAUTH_VNODE_READ_SECURITY,
        KAUTH_VNODE_WRITE_SECURITY,
        KAUTH_VNODE_TAKE_OWNERSHIP
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                testFileVnode.get(),
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testDeleteFile {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
    XCTAssertTrue(MockCalls::DidCallFunctionsInOrder(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
            _,
            MessageType_KtoU_HydrateFile,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr),
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
            _,
            MessageType_KtoU_NotifyFilePreDelete,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr))
    );
    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 2);
}

- (void) testDeleteDir {
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
    XCTAssertTrue(MockCalls::DidCallFunctionsInOrder(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
            _,
            MessageType_KtoU_RecursivelyEnumerateDirectory,
            testDirVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr),
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
            _,
            MessageType_KtoU_NotifyDirectoryPreDelete,
            testDirVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr))
    );
    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 2);
}

- (void) testEmptyDirectoryEnumerates {
    testDirVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    const int actionCount = 5;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_LIST_DIRECTORY,
        KAUTH_VNODE_SEARCH,
        KAUTH_VNODE_READ_SECURITY,
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertTrue(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_EnumerateDirectory,
                testDirVnode.get(),
                _,
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testEventsThatShouldNotDirectoryEnumerates {
    testDirVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    const int actionCount = 9;
    kauth_action_t actions[actionCount] =
    {
       KAUTH_VNODE_WRITE_DATA,
       KAUTH_VNODE_ADD_FILE,
       KAUTH_VNODE_APPEND_DATA,
       KAUTH_VNODE_ADD_SUBDIRECTORY,
       KAUTH_VNODE_DELETE_CHILD,
       KAUTH_VNODE_WRITE_ATTRIBUTES,
       KAUTH_VNODE_WRITE_EXTATTRIBUTES,
       KAUTH_VNODE_WRITE_SECURITY,
       KAUTH_VNODE_TAKE_OWNERSHIP
    };

    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_EnumerateDirectory,
                testDirVnode.get(),
                _,
                _,
                _,
                _,
                _,
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testNonEmptyDirectoryDoesNotEnumerate {
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    const int actionCount = 5;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_LIST_DIRECTORY,
        KAUTH_VNODE_SEARCH,
        KAUTH_VNODE_READ_SECURITY,
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
    }
}
    
-(void) testWriteFile {
    // If we have FileXattrData attribute we should trigger MessageType_KtoU_NotifyFilePreConvertToFull to remove it
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
        XCTAssertTrue(MockCalls::DidCallFunctionsInOrder(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
            _,
            MessageType_KtoU_HydrateFile,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr),
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        make_tuple(
           _,
            MessageType_KtoU_NotifyFilePreConvertToFull,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr))
    );
    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 2);
}

-(void) testWriteFileHydrated {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    // If we have FileXattrData attribute we should trigger MessageType_KtoU_NotifyFilePreConvertToFull to remove it
    SetPrjFSFileXattrData(testFileVnode);
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
    XCTAssertFalse(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_HydrateFile,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr));
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyFilePreConvertToFull,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr));
    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

-(void) testWriteFileFull {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testEventsAreIgnored {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot | FileFlags_IsEmpty;
    const int actionCount = 3;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_WRITE_SECURITY,
        KAUTH_VNODE_TAKE_OWNERSHIP,
        KAUTH_VNODE_ACCESS
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        // Verify for File node
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertFalse(
            MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
        MockCalls::Clear();

        // Verify for Directory node
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0);
        XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
        MockCalls::Clear();
    }
}

- (void) testDeleteWithNoVirtualizationRoot {
    [self removeAllVirtualizationRoots];
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testDeleteWhenRequestFails {
    ProviderMessageMock_SetDefaultRequestResult(false);
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

   XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

- (void) testDeleteDirectoryWithDisappearingVirtualizationRoot {
    ProviderMessageMock_SetRequestSideEffect(
        [&]()
        {
            ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
        });
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

// When the first call to getVirtualizationRoot fails, ensure no more calls are made
- (void) testDeleteDirectoryWhenFirstRequestFails {
    ProviderMessageMock_SetDefaultRequestResult(false);
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

- (void) testDeleteDirectoryWhenSecondRequestFails {
    ProviderMessageMock_SetSecondRequestResult(false);
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 2);
}

- (void) testDeleteDirectoryWithNoVirtualizationRoot {
    [self removeAllVirtualizationRoots];
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testReadAttributesDirectoryWithNoVirtualizationRoot {
    [self removeAllVirtualizationRoots];
    testDirVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_READ_ATTRIBUTES,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testReadAttributesDirectoryWhenRequestFails {
    ProviderMessageMock_SetDefaultRequestResult(false);
    testDirVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_READ_ATTRIBUTES,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

- (void) testReadAttributesWithNoVirtualizationRoot {
    [self removeAllVirtualizationRoots];
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_READ_ATTRIBUTES,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

- (void) testReadAttributesWhenRequestFails {
    ProviderMessageMock_SetDefaultRequestResult(false);
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_READ_ATTRIBUTES,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

- (void) testWriteWithDisappearingVirtualizationRoot {
    ProviderMessageMock_SetRequestSideEffect(
        [&]()
        {
            ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
        });
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 1);
}

- (void) testWriteWhenSecondRequestFails {
    ProviderMessageMock_SetSecondRequestResult(false);
    SetPrjFSFileXattrData(testFileVnode);
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DENY);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 2);
}

- (void) testWriteDataToForkedPath {
    // By setting namedStream, HandleVnodeOperation with treat testFileVnode as a named fork
    testFileVnode->namedStream = true;
    testFileVnode->GetParentVnode()->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;

    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 0);
}

- (void) testIneligibleFilesystemType
{
    shared_ptr<mount> testMountNone = mount::Create("msdos", fsid_t{}, 0);
    shared_ptr<vnode> testVnodeNone = vnode::Create(testMountNone, "/Volumes/USBSTICK");
    XCTAssertEqual(
        KAUTH_RESULT_DEFER,
        HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_ADD_FILE,
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testVnodeNone.get()),
            0,
            0));
    XCTAssertFalse(MockCalls::DidCallFunction(ProviderMessaging_TrySendRequestAndWaitForResponse));
}

@end
