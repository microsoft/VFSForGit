#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include "../PrjFSKext/KauthHandlerTestable.hpp"
#include "../PrjFSKext/PerformanceTracing.hpp"
#include "../PrjFSKext/VirtualizationRootsTestable.hpp"
#import "KextAssertIntegration.h"
#import <sys/stat.h>
#include "KextLogMock.h"
#include "KextMockUtilities.hpp"
#include "MockVnodeAndMount.hpp"
#include "MockProc.hpp"
#include "VnodeCacheEntriesWrapper.hpp"

using std::shared_ptr;
using std::string;

@interface KauthHandlerTests : PFSKextTestCase
@end

@implementation KauthHandlerTests
{
    VnodeCacheEntriesWrapper cacheWrapper;
    vfs_context_t context;
}

- (void) setUp {
    [super setUp];
    kern_return_t initResult = VirtualizationRoots_Init();
    XCTAssertEqual(initResult, KERN_SUCCESS);
    self->cacheWrapper.AllocateCache();
    context = vfs_context_create(nullptr);
    MockProcess_AddContext(context, 501 /*pid*/);
    MockProcess_SetSelfPid(501);
    MockProcess_AddProcess(501 /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
}

- (void) tearDown {
    MockProcess_Reset();
    self->cacheWrapper.FreeCache();
    MockVnodes_CheckAndClear();
    VirtualizationRoots_Cleanup();
    MockCalls::Clear();
    [super tearDown];
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
    shared_ptr<mount> testMount = mount::Create();
    shared_ptr<vnode> testVnode = testMount->CreateVnode("/foo");
    XCTAssertTrue(ShouldIgnoreVnodeType(VNON, testVnode.get()));
    XCTAssertTrue(ShouldIgnoreVnodeType(VBLK, testVnode.get()));
    XCTAssertTrue(ShouldIgnoreVnodeType(VCHR, testVnode.get()));
    XCTAssertTrue(ShouldIgnoreVnodeType(VSOCK, testVnode.get()));
    XCTAssertTrue(ShouldIgnoreVnodeType(VFIFO, testVnode.get()));
    XCTAssertTrue(ShouldIgnoreVnodeType(VBAD, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(VREG, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(VDIR, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(VLNK, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(VSTR, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(VCPLX, testVnode.get()));
    XCTAssertFalse(ShouldIgnoreVnodeType(static_cast<vtype>(1000), testVnode.get()));
}

- (void)testFileFlaggedInRoot {
    bool fileFlaggedInRoot;
    shared_ptr<mount> testMount = mount::Create();
    shared_ptr<vnode> testVnode = vnode::Create(testMount, "/foo");
    vfs_context_t _Nonnull context = vfs_context_create(nullptr);
    
    testVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
    XCTAssertTrue(fileFlaggedInRoot);

    testVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot | UF_NODUMP;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
    XCTAssertTrue(fileFlaggedInRoot);

    testVnode->attrValues.va_flags = FileFlags_IsEmpty;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
    XCTAssertFalse(fileFlaggedInRoot);
    
    testVnode->attrValues.va_flags = FileFlags_Invalid;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
    XCTAssertFalse(fileFlaggedInRoot);
    
    testVnode->attrValues.va_flags = 0x00000100;
    XCTAssertTrue(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
    XCTAssertFalse(fileFlaggedInRoot);

    testVnode->errors.getattr = EBADF;
    XCTAssertFalse(TryGetFileIsFlaggedAsInRoot(testVnode.get(), context, &fileFlaggedInRoot));
}

- (void)testShouldHandleVnodeOpEvent {
    // In Parameters
    shared_ptr<mount> testMount = mount::Create();
    shared_ptr<vnode> testVnode = vnode::Create(testMount, "/foo");
    testVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    PerfTracer perfTracer;
    vfs_context_t _Nonnull context = vfs_context_create(nullptr);
    kauth_action_t action = KAUTH_VNODE_READ_DATA;
    
    // Out Parameters
    uint32_t vnodeFileFlags;
    int pid;
    char procname[MAXCOMLEN + 1] = "";
    int kauthResult;
    int kauthError;

    
    // Test Success Case
    XCTAssertTrue(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);
    
    
    // Test KAUTH_VNODE_ACCESS is not handled
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            KAUTH_VNODE_ACCESS,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);

    // Test KAUTH_VNODE_ACCESS | KAUTH_VNODE_READ_DATA is not handled
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            KAUTH_VNODE_ACCESS | KAUTH_VNODE_READ_DATA,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);
    
    // Test invalid File System
    shared_ptr<mount> testMountNone = mount::Create("none", fsid_t{}, 0);
    shared_ptr<vnode> testVnodeNone = vnode::Create(testMountNone, "/none");
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnodeNone.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);
    
    
    // Test invalid VNODE Type
    shared_ptr<vnode> testVnodeInvalidType = vnode::Create(testMount, "/foo2", VNON);
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnodeInvalidType.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);

    
    // Test failure reading attr
    testVnode->errors.getattr = EBADF;
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DENY);
    // reset to valid value
    testVnode->errors.getattr = 0;

    
    // Test invalid file flag
    testVnode->attrValues.va_flags = FileFlags_IsEmpty;
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);
    // reset to valid value
    testVnode->attrValues.va_flags = FileFlags_IsInVirtualizationRoot;
    

    // Test with file crawler trying to populate an empty file
    testVnode->attrValues.va_flags = FileFlags_IsEmpty | FileFlags_IsInVirtualizationRoot;
    MockProcess_Reset();
    MockProcess_SetSelfPid(501);
    MockProcess_AddContext(context, 501 /*pid*/);
    MockProcess_AddProcess(501 /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "mds" /*name*/);
    XCTAssertFalse(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DENY);
    
    // Test with finder trying to populate an empty file
    MockProcess_Reset();
    MockProcess_SetSelfPid(501);
    MockProcess_AddContext(context, 501 /*pid*/);
    MockProcess_AddProcess(501 /*pid*/, 1 /*credentialId*/, 1 /*ppid*/, "Finder" /*name*/);
    XCTAssertTrue(
        ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            testVnode.get(),
            action,
            &vnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            &kauthError));
    XCTAssertEqual(kauthResult, KAUTH_RESULT_DEFER);
}

- (void)testCurrentProcessWasSpawnedByRegularUser {
    // Defaults should pass for all tests
    XCTAssertTrue(CurrentProcessWasSpawnedByRegularUser());
    MockProcess_Reset();

    // Process is a service user and does not have a parent
    MockProcess_AddContext(context, 500 /*pid*/);
    MockProcess_SetSelfPid(500);
    MockProcess_AddCredential(1 /*credentialId*/, 1 /*UID*/);
    MockProcess_AddProcess(500 /*pid*/, 1 /*credentialId*/, 501 /*ppid*/, "test" /*name*/);
    XCTAssertFalse(CurrentProcessWasSpawnedByRegularUser());
    MockProcess_Reset();

    // Test a process with a service UID, valid parent pid, but proc_find fails to find parent pid
    MockCalls::Clear();
    MockProcess_AddContext(context, 500 /*pid*/);
    MockProcess_SetSelfPid(500);
    MockProcess_AddCredential(1 /*credentialId*/, 1 /*UID*/);
    MockProcess_AddProcess(500 /*pid*/, 1 /*credentialId*/, 501 /*ppid*/, "test" /*name*/);
    XCTAssertFalse(CurrentProcessWasSpawnedByRegularUser());
    XCTAssertTrue(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_ERROR));
    MockProcess_Reset();

    // 'sudo' scenario: Root process with non-root parent
    MockProcess_AddContext(context, 502 /*pid*/);
    MockProcess_SetSelfPid(502);
    MockProcess_AddCredential(1 /*credentialId*/, 1 /*UID*/);
    MockProcess_AddCredential(2 /*credentialId*/, 501 /*UID*/);
    MockProcess_AddProcess(502 /*pid*/, 1 /*credentialId*/, 501 /*ppid*/, "test" /*name*/);
    MockProcess_AddProcess(501 /*pid*/, 2 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
    XCTAssertTrue(CurrentProcessWasSpawnedByRegularUser());
    MockProcess_Reset();

    // Process and it's parent are service users
    MockProcess_AddContext(context, 502 /*pid*/);
    MockProcess_SetSelfPid(502);
    MockProcess_AddCredential(1 /*credentialId*/, 1 /*UID*/);
    MockProcess_AddCredential(2 /*credentialId*/, 2 /*UID*/);
    MockProcess_AddProcess(502 /*pid*/, 1 /*credentialId*/, 501 /*ppid*/, "test" /*name*/);
    MockProcess_AddProcess(501 /*pid*/, 2 /*credentialId*/, 1 /*ppid*/, "test" /*name*/);
    XCTAssertFalse(CurrentProcessWasSpawnedByRegularUser());
}

- (void)testUseMainForkIfNamedStream {
    shared_ptr<mount> testMount = mount::Create();
    const char* filePath = "/Users/test/code/Repo/file";
    shared_ptr<vnode> testFileVnode = testMount->CreateVnodeTree(filePath);

    // Out Parameters to test
    bool putVnodeWhenDone;
    vnode_t testVnode = testFileVnode.get();

    testVnode->namedStream = false;
    UseMainForkIfNamedStream(testVnode, putVnodeWhenDone);
    XCTAssertFalse(putVnodeWhenDone);
    XCTAssertTrue(testVnode == testFileVnode.get());

    testVnode->namedStream = true;
    UseMainForkIfNamedStream(testVnode, putVnodeWhenDone);
    XCTAssertTrue(putVnodeWhenDone);
    XCTAssertTrue(testVnode == testFileVnode->GetParentVnode().get());
    vnode_put(testVnode);
}

- (void)testResizePendingRenamesRace
{
    InitPendingRenames();
    XCTAssertLessThan(s_maxPendingRenames, 16);
    ResizePendingRenames(16);
    ResizePendingRenames(16);
    XCTAssertEqual(s_maxPendingRenames, 16);
    CleanupPendingRenames();
}

- (void)testResizePendingRenamesLogsLargeArrayError
{
    shared_ptr<mount> testMount = mount::Create();
    string filePath = "/Users/test/code/Repo/file";
    shared_ptr<vnode> testFile = testMount->CreateVnodeTree(filePath);

    InitPendingRenames();
    ResizePendingRenames(16);
    s_pendingRenameCount = 16; // pretend we're full
    XCTAssertFalse(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_ERROR));
    RecordPendingRenameOperation(testFile.get());
    XCTAssertTrue(MockCalls::DidCallFunction(KextMessageLogged, KEXTLOG_ERROR));
    XCTAssertEqual(s_pendingRenameCount, 17);
    XCTAssertGreaterThan(s_maxPendingRenames, 16);
    XCTAssertTrue(DeleteOpIsForRename(testFile.get()));
    XCTAssertEqual(s_pendingRenameCount, 16);
    s_pendingRenameCount = 0;
    CleanupPendingRenames();
}

- (void)testPendingRenamesOutOfOrderInsertAndRemoval
{
    shared_ptr<mount> testMount = mount::Create();
    string file1Path = "/Users/test/code/Repo/file1";
    string file2Path = "/Users/test/code/Repo/file2";
    shared_ptr<vnode> testFile1 = testMount->CreateVnodeTree(file1Path);
    shared_ptr<vnode> testFile2 = testMount->CreateVnodeTree(file2Path);

    InitPendingRenames();
    RecordPendingRenameOperation(testFile1.get());
    MockProcess_SetCurrentThreadIndex(1);
    RecordPendingRenameOperation(testFile2.get());
    MockProcess_SetCurrentThreadIndex(0);
    XCTAssertTrue(DeleteOpIsForRename(testFile1.get()));
    MockProcess_SetCurrentThreadIndex(1);
    XCTAssertTrue(DeleteOpIsForRename(testFile2.get()));
    MockProcess_SetCurrentThreadIndex(0);
    XCTAssertEqual(s_pendingRenameCount, 0);
    CleanupPendingRenames();
}

@end
