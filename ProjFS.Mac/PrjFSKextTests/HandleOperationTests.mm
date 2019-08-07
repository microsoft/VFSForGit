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
using std::extent;
using KextMock::_;

// Darwin version of running kernel
int version_major = PrjFSDarwinMajorVersion::MacOS10_14_Mojave;

static const pid_t RunningMockProcessPID = 501;

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

static void TestForDarwinVersionRange(int versionMin, int versionMax, void(^testBlock)(void))
{
    const int savedVersion = version_major;
    for (int version = versionMin; version <= versionMax; ++version)
    {
        version_major = version;
        testBlock();
    }
    version_major = savedVersion;
}

static void TestForAllSupportedDarwinVersions(void(^testBlock)(void))
{
    TestForDarwinVersionRange(PrjFSDarwinMajorVersion::MacOS10_13_HighSierra, PrjFSDarwinMajorVersion::MacOS10_15_Catalina, testBlock);
}

@interface HandleVnodeOperationTests : PFSKextTestCase
@end

@implementation HandleVnodeOperationTests
{
    vfs_context_t context;
    string repoPath;
    string filePath;
    string dirPath;
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
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&dummyClient, dummyClientPid, repoPath.c_str());
    XCTAssertEqual(result.error, 0);
    self->dummyRepoHandle = result.root;

    MockProcess_AddContext(context, RunningMockProcessPID /*pid*/);
    MockProcess_SetSelfInfo(RunningMockProcessPID, "Test");
    MockProcess_AddProcess(RunningMockProcessPID, 1 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
    
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
    CleanupPendingRenames();
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
    
    kauth_action_t actions[] =
    {
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_WRITE_ATTRIBUTES,
        KAUTH_VNODE_READ_EXTATTRIBUTES,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES,
        KAUTH_VNODE_READ_DATA,
        KAUTH_VNODE_WRITE_DATA,
        KAUTH_VNODE_EXECUTE,
        KAUTH_VNODE_APPEND_DATA,
    };
    const int actionCount = std::extent<decltype(actions)>::value;

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

- (void) testFileDeleteHydratesOnlyWhenNecessary
{
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    
    TestForAllSupportedDarwinVersions(^{
        InitPendingRenames();
        XCTAssertEqual(
            KAUTH_RESULT_DEFER,
            HandleVnodeOperation(
                nullptr,
                nullptr,
                KAUTH_VNODE_DELETE,
                reinterpret_cast<uintptr_t>(self->context),
                reinterpret_cast<uintptr_t>(self->testFileVnode.get()),
                0,
                0));
        bool didHydrate =
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                self->testFileVnode.get(),
                _,
                _,
                _,
                _,
                _,
                _,
                nullptr);

        // On High Sierra, any delete causes hydration as it might be due to a rename:
        if (version_major <= PrjFSDarwinMajorVersion::MacOS10_13_HighSierra)
        {
            XCTAssertTrue(didHydrate);
        }
        else
        {
            // On Mojave+, no hydration on non-rename delete:
            XCTAssertFalse(didHydrate);
        }
        
        MockCalls::Clear();
        CleanupPendingRenames();
    });
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

- (void) testHydrationOnDeleteWhenRenamingFile {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;

    // Hydration on delete only occurs if deletion is caused by rename
    string renamedFilePath = filePath + "_renamed";
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath.c_str()),
        reinterpret_cast<uintptr_t>(renamedFilePath.c_str()),
        0); // unused

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
            MessageType_KtoU_NotifyFilePreDeleteFromRename,
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

- (void) testDeleteFileNonRenamed
{
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;

    InitPendingRenames();
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);
    XCTAssertTrue(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        _,
        MessageType_KtoU_NotifyFilePreDelete,
        testFileVnode.get(),
        _,
        _,
        _,
        _,
        _,
        _,
        nullptr));
    
    // Should not hydrate if delete is not caused by rename
    XCTAssertFalse(MockCalls::DidCallFunction(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        _,
        MessageType_KtoU_HydrateFile,
        _,
        _,
        _,
        _,
        _,
        _,
        _,
        nullptr));
    CleanupPendingRenames();
}

- (void) testConcurrentRenameOperationRecording
{
    InitPendingRenames();
    self->testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;

    string otherFilePath = repoPath + "/otherFile";
    shared_ptr<vnode> otherTestFileVnode = testMount->CreateVnodeTree(otherFilePath);
    otherTestFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;

    string renamedFilePath = filePath + "_renamed";
    string renamedOtherFilePath = otherFilePath + "_renamed";

    MockProcess_SetCurrentThreadIndex(0);
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(filePath.c_str()),
        reinterpret_cast<uintptr_t>(renamedFilePath.c_str()),
        0); // unused

    MockProcess_SetCurrentThreadIndex(1);
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(otherTestFileVnode.get()),
        reinterpret_cast<uintptr_t>(otherFilePath.c_str()),
        reinterpret_cast<uintptr_t>(renamedOtherFilePath.c_str()),
        0); // unused

    MockProcess_SetCurrentThreadIndex(0);
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0) == KAUTH_RESULT_DEFER);

    MockProcess_SetCurrentThreadIndex(1);
    XCTAssertTrue(HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(otherTestFileVnode.get()),
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
            MessageType_KtoU_NotifyFilePreDeleteFromRename,
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
            MessageType_KtoU_HydrateFile,
            otherTestFileVnode.get(),
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
            MessageType_KtoU_NotifyFilePreDeleteFromRename,
            otherTestFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr))
    );
    XCTAssertTrue(MockCalls::CallCount(ProviderMessaging_TrySendRequestAndWaitForResponse) == 4);
    CleanupPendingRenames();
}


- (void) testHydrationOnDeleteWhenRenamingDirectory
{
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    // Hydration on delete only occurs if deletion is caused by rename
    string renamedDirPath = self->dirPath + "_renamed";
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(self->testDirVnode.get()),
        reinterpret_cast<uintptr_t>(self->dirPath.c_str()),
        reinterpret_cast<uintptr_t>(renamedDirPath.c_str()),
        0); // unused

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

- (void) testDeleteDirNonRenamed
{
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    TestForAllSupportedDarwinVersions(^{
        InitPendingRenames();
        XCTAssertTrue(HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_DELETE,
            reinterpret_cast<uintptr_t>(self->context),
            reinterpret_cast<uintptr_t>(self->testDirVnode.get()),
            0,
            0) == KAUTH_RESULT_DEFER);
        XCTAssertTrue(MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyDirectoryPreDelete,
            self->testDirVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr));

        // Should not enumerate if delete is not caused by rename, except on High Sierra
        bool didEnumerate = MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_RecursivelyEnumerateDirectory,
            self->testDirVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr);
        if (version_major <= PrjFSDarwinMajorVersion::MacOS10_13_HighSierra)
        {
            XCTAssertTrue(didEnumerate);
        }
        else
        {
            XCTAssertFalse(didEnumerate);
        }
        
        MockCalls::Clear();
        CleanupPendingRenames();
    });
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

-(void) testWriteFileHydratedOfflineRoot
{
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);
    ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
    
    XCTAssertEqual(
        KAUTH_RESULT_DENY,
        HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_WRITE_DATA,
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0));
    XCTAssertFalse(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            _,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr));
}

-(void) testWriteFlaggedNonRepoFile
{
    string testFilePath = "/Users/test/code/otherproject/file";
    shared_ptr<vnode> testFile = testMount->CreateVnodeTree(testFilePath);
    testFile->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertEqual(
        KAUTH_RESULT_DEFER,
        HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_WRITE_DATA,
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFile.get()),
            0,
            0));
    XCTAssertFalse(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            _,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            _,
            nullptr));
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
- (void) testRenameDirectoryWhenFirstRequestFails {
    ProviderMessageMock_SetDefaultRequestResult(false);
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;

    string renamedDirPath = self->dirPath + "_renamed";
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(self->testDirVnode.get()),
        reinterpret_cast<uintptr_t>(self->dirPath.c_str()),
        reinterpret_cast<uintptr_t>(renamedDirPath.c_str()),
        0); // unused

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

- (void) testDeleteDirectoryForRenameWhenSecondRequestFails {
    ProviderMessageMock_SetSecondRequestResult(false);
    
    // Hydration on delete only occurs if deletion is caused by rename
    string renamedDirPath = self->dirPath + "_renamed";
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, /* idata, unused */
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(self->testDirVnode.get()),
        reinterpret_cast<uintptr_t>(self->dirPath.c_str()),
        reinterpret_cast<uintptr_t>(renamedDirPath.c_str()),
        0); // unused

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

- (void) testRenameDirectoryWithNoVirtualizationRoot {
    [self removeAllVirtualizationRoots];
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
 
    string renamedDirPath = self->dirPath + "_renamed";
    HandleFileOpOperation(
        nullptr, // credential
        nullptr, // idata, unused
        KAUTH_FILEOP_WILL_RENAME,
        reinterpret_cast<uintptr_t>(self->testDirVnode.get()),
        reinterpret_cast<uintptr_t>(self->dirPath.c_str()),
        reinterpret_cast<uintptr_t>(renamedDirPath.c_str()),
        0); // unused

    XCTAssertEqual(
        KAUTH_RESULT_DEFER,
        HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_DELETE,
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0));

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
    // Tests provider disappearing between hydration and attempting to convert to full.
    
    // Start with empty file…
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(self->testFileVnode);
    
    // First message to provider marks the file as hydrated and also disconnects the providers
    ProviderMessageMock_SetRequestSideEffect(
        [&]()
        {
            ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);
            // mark as hydrated
            testFileVnode->attrValues.va_flags &= ~FileFlags_IsEmpty;
        });
    
    // File should become hydrated but write access should be denied due to failure to convert to full.
    XCTAssertEqual(
        KAUTH_RESULT_DENY,
        HandleVnodeOperation(
            nullptr,
            nullptr,
            KAUTH_VNODE_WRITE_DATA,
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0));

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
    shared_ptr<vnode> testVnodeNone = testMountNone->CreateVnodeTree("/Volumes/USBSTICK", VDIR);
    
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

- (void) testOfflineRootDeniesAccessToEmptyFile
{
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);

    ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);

    kauth_action_t actions[] =
    {
        KAUTH_VNODE_WRITE_ATTRIBUTES,
        KAUTH_VNODE_WRITE_EXTATTRIBUTES,
        KAUTH_VNODE_WRITE_DATA,
        KAUTH_VNODE_APPEND_DATA,
        KAUTH_VNODE_READ_DATA,
        KAUTH_VNODE_READ_ATTRIBUTES,
        KAUTH_VNODE_EXECUTE,
        KAUTH_VNODE_READ_EXTATTRIBUTES,
    };
    const size_t actionCount = extent<decltype(actions)>::value;
    
    for (size_t i = 0; i < actionCount; i++)
    {
        XCTAssertEqual(
            KAUTH_RESULT_DENY,
            HandleVnodeOperation(
                nullptr,
                nullptr,
                actions[i],
                reinterpret_cast<uintptr_t>(context),
                reinterpret_cast<uintptr_t>(testFileVnode.get()),
                0,
                0));
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
        MockCalls::Clear();
    }
}

- (void) testOfflineRootDeniesRename
{
    // Where we can detect renames (Mojave and newer), file/directory renames
    // should be prevented when the provider is offline. On High Sierra we have
    // to let them happen as we can't distinguish them from file deletions
    // before it's already happened.

    ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);

    // Check the behaviour works for both empty and full files, and empty files are not hydrated.
    vector<uint32_t> vnode_flags { FileFlags_IsInVirtualizationRoot, FileFlags_IsInVirtualizationRoot | FileFlags_IsEmpty };
    
    std::for_each(vnode_flags.begin(), vnode_flags.end(),
        [self](uint32_t flags)
        {
            testFileVnode->attrValues.va_flags = flags;

            TestForAllSupportedDarwinVersions(^{
                InitPendingRenames();

                if (version_major >= PrjFSDarwinMajorVersion::MacOS10_14_Mojave)
                {
                    string renamedFilePath = self->filePath + "_renamed";
                    HandleFileOpOperation(
                        nullptr, // credential
                        nullptr, /* idata, unused */
                        KAUTH_FILEOP_WILL_RENAME,
                        reinterpret_cast<uintptr_t>(self->testFileVnode.get()),
                        reinterpret_cast<uintptr_t>(self->filePath.c_str()),
                        reinterpret_cast<uintptr_t>(renamedFilePath.c_str()),
                        0); // unused
                }

                int deleteAuthResult =
                    HandleVnodeOperation(
                        nullptr,
                        nullptr,
                        KAUTH_VNODE_DELETE,
                        reinterpret_cast<uintptr_t>(self->context),
                        reinterpret_cast<uintptr_t>(self->testFileVnode.get()),
                        0,
                        0);

                if (version_major <= PrjFSDarwinMajorVersion::MacOS10_13_HighSierra)
                {
                    XCTAssertEqual(deleteAuthResult, KAUTH_RESULT_DEFER);
                }
                else
                {
                    // On Mojave+, renames should be blocked:
                    XCTAssertEqual(deleteAuthResult, KAUTH_RESULT_DENY, "flags = 0x%x", flags);
                }

                XCTAssertFalse(
                    MockCalls::DidCallFunction(
                        ProviderMessaging_TrySendRequestAndWaitForResponse,
                        _,
                        MessageType_KtoU_HydrateFile,
                        self->testFileVnode.get(),
                        _,
                        _,
                        _,
                        _,
                        _,
                        _,
                        nullptr));

                MockCalls::Clear();
                CleanupPendingRenames();
            });
        });
}

- (void) testOfflineRootAllowsRegisteredProcessAccessToEmptyFile
{
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetPrjFSFileXattrData(testFileVnode);

    ActiveProvider_Disconnect(self->dummyRepoHandle, &self->dummyClient);

    XCTAssertTrue(VirtualizationRoots_AddOfflineIOProcess(RunningMockProcessPID));

    kauth_action_t actions[] =
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
    const size_t actionCount = extent<decltype(actions)>::value;
    
    for (size_t i = 0; i < actionCount; i++)
    {
        XCTAssertEqual(
            KAUTH_RESULT_DEFER,
            HandleVnodeOperation(
                nullptr,
                nullptr,
                actions[i],
                reinterpret_cast<uintptr_t>(context),
                reinterpret_cast<uintptr_t>(testFileVnode.get()),
                0,
                0));
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
        MockCalls::Clear();
    }

    VirtualizationRoots_RemoveOfflineIOProcess(RunningMockProcessPID);
}

@end
