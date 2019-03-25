#include "../PrjFSKext/KauthHandlerTestable.hpp"
#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/PrjFSProviderUserClient.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#include "../PrjFSKext/PerformanceTracing.hpp"
#include "../PrjFSKext/public/Message.h"
#include "../PrjFSKext/ProviderMessaging.hpp"
#include "../PrjFSKext/public/PrjFSXattrs.h"
#import <XCTest/XCTest.h>
#import <sys/stat.h>
#include "KextMockUtilities.hpp"
#include "MockVnodeAndMount.hpp"
#include "MockProc.hpp"

using std::shared_ptr;
using std::vector;
using KextMock::_;

class PrjFSProviderUserClient
{
};

bool ProviderMessaging_TrySendRequestAndWaitForResponse(
    VirtualizationRootHandle root,
    MessageType messageType,
    const vnode_t vnode,
    const FsidInode& vnodeFsidInode,
    const char* vnodePath,
    int pid,
    const char* procname,
    int* kauthResult,
    int* kauthError)
{
    MockCalls::RecordFunctionCall(
        ProviderMessaging_TrySendRequestAndWaitForResponse,
        root,
        messageType,
        vnode,
        vnodeFsidInode,
        vnodePath,
        pid,
        procname,
        kauthResult,
        kauthError);
    
    return true;
}

static void SetFileXattrData(shared_ptr<vnode> vnode)
{
    PrjFSFileXAttrData rootXattr = {};
    vector<uint8_t> rootXattrData(sizeof(rootXattr), 0x00);
    memcpy(rootXattrData.data(), &rootXattr, rootXattrData.size());
    vnode->xattrs.insert(make_pair(PrjFSFileXAttrName, rootXattrData));
}

@interface HandleVnodeOperationTests : XCTestCase
@end

@implementation HandleVnodeOperationTests
{
    vfs_context_t context;
    const char* repoPath;
    const char* filePath;
    const char* dirPath;
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    shared_ptr<mount> testMount;
    shared_ptr<vnode> repoRootVnode;
    shared_ptr<vnode> testFileVnode;
    shared_ptr<vnode> testDirVnode;
}

- (void) setUp
{
    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
    context = vfs_context_create(NULL);
    dummyClientPid = 100;

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
    vnode_put(s_virtualizationRoots[result.root].rootVNode);
}

- (void) tearDown
{
    testMount.reset();
    repoRootVnode.reset();
    testFileVnode.reset();
    testDirVnode.reset();
    VirtualizationRoots_Cleanup();
    vfs_context_rele(context);
    MockVnodes_CheckAndClear();
    MockCalls::Clear();
}

- (void) testEmptyFileHydrates {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    
    const int actionCount = 8;
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
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0);
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
                nullptr));
        MockCalls::Clear();
    }
}

- (void) testNonEmptyFileDoesNotHydrate {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    
    const int actionCount = 8;
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
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0);
        XCTAssertFalse(
            MockCalls::DidCallFunction(
                ProviderMessaging_TrySendRequestAndWaitForResponse,
                _,
                MessageType_KtoU_HydrateFile,
                _,
                _,
                _,
                _,
                _,
                _,
                _));
        MockCalls::Clear();
    }
}

- (void) testDeleteFile {
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0);
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyFilePreDelete,
            testFileVnode.get(),
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
            MessageType_KtoU_HydrateFile,
            testFileVnode.get(),
            _,
            _,
            _,
            _,
            _,
            nullptr));
}

- (void) testDeleteDir {
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_DELETE,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testDirVnode.get()),
        0,
        0);
    XCTAssertTrue(
       MockCalls::DidCallFunction(
            ProviderMessaging_TrySendRequestAndWaitForResponse,
            _,
            MessageType_KtoU_NotifyDirectoryPreDelete,
            testDirVnode.get(),
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
            MessageType_KtoU_RecursivelyEnumerateDirectory,
            testDirVnode.get(),
            _,
            _,
            _,
            _,
            _,
            nullptr));
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
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0);
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
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testDirVnode.get()),
            0,
            0);
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
    
-(void) testWriteFile {
    // If we have FileXattrData attribute we should trigger MessageType_KtoU_NotifyFilePreConvertToFull to remove it
    testFileVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    SetFileXattrData(testFileVnode);
    HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0);
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
            nullptr));
}

-(void) testWriteFileHydrated {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    // If we have FileXattrData attribute we should trigger MessageType_KtoU_NotifyFilePreConvertToFull to remove it
    SetFileXattrData(testFileVnode);
    HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0);
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
            nullptr));
}

-(void) testWriteFileFull {
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    HandleVnodeOperation(
        nullptr,
        nullptr,
        KAUTH_VNODE_WRITE_DATA,
        reinterpret_cast<uintptr_t>(context),
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        0,
        0);
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
    XCTAssertFalse(
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
            nullptr));
}

- (void) testEventsAreIgnored {
    testDirVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot | FileFlags_IsEmpty;
    const int actionCount = 5;
    kauth_action_t actions[actionCount] =
    {
        KAUTH_VNODE_APPEND_DATA,
        KAUTH_VNODE_ADD_SUBDIRECTORY,
        KAUTH_VNODE_WRITE_SECURITY,
        KAUTH_VNODE_TAKE_OWNERSHIP,
        KAUTH_VNODE_ACCESS
    };
    
    for (int i = 0; i < actionCount; i++)
    {
        // Verify for File node
        HandleVnodeOperation(
            nullptr,
            nullptr,
            actions[i],
            reinterpret_cast<uintptr_t>(context),
            reinterpret_cast<uintptr_t>(testFileVnode.get()),
            0,
            0);
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

- (void) testOpen {
    // A file that is not yet in the virtual root, should recieve a creation request
    char pathBuffer[PrjFSMaxPath] = "";
    int pathLength = sizeof(pathBuffer);
    vn_getpath(testFileVnode.get(), pathBuffer, &pathLength);

    HandleFileOpOperation(
        nullptr,
        nullptr,
        1,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(pathBuffer),
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
    // A file that is already in the virtual root, should not recieve a creation request
    testFileVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    char pathBuffer[PrjFSMaxPath] = "";
    int pathLength = sizeof(pathBuffer);
    vn_getpath(testFileVnode.get(), pathBuffer, &pathLength);

    HandleFileOpOperation(
        nullptr,
        nullptr,
        1,
        reinterpret_cast<uintptr_t>(testFileVnode.get()),
        reinterpret_cast<uintptr_t>(pathBuffer),
        0,
        0);
    
    XCTAssertFalse(
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


@end
