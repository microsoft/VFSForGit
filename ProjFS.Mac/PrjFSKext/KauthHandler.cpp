#include <kern/debug.h>
#include <sys/kauth.h>
#include <sys/proc.h>
#include <libkern/OSAtomic.h>
#include <kern/assert.h>
#include <stdatomic.h>

#include "public/PrjFSCommon.h"
#include "public/PrjFSPerfCounter.h"
#include "VirtualizationRoots.hpp"
#include "VnodeUtilities.hpp"
#include "KauthHandler.hpp"
#include "public/Message.h"
#include "Locks.hpp"
#include "PrjFSProviderUserClient.hpp"
#include "PerformanceTracing.hpp"
#include "kernel-header-wrappers/mount.h"
#include "KextLog.hpp"
#include "ProviderMessaging.hpp"
#include "public/PrjFSXattrs.h"

#ifdef KEXT_UNIT_TESTING
#include "KauthHandlerTestable.hpp"
#endif

enum ProviderCallbackPolicy
{
    CallbackPolicy_AllowAny,
    CallbackPolicy_UserInitiatedOnly,
};

// Function prototypes
KEXT_STATIC int HandleVnodeOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3);

KEXT_STATIC int HandleFileOpOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3);

static int GetPid(vfs_context_t _Nonnull context);

static bool TryReadVNodeFileFlags(vnode_t vn, vfs_context_t _Nonnull context, uint32_t* flags);
KEXT_STATIC_INLINE bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit);
KEXT_STATIC_INLINE bool TryGetFileIsFlaggedAsInRoot(vnode_t vnode, vfs_context_t _Nonnull context, bool* flaggedInRoot);
KEXT_STATIC_INLINE bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask);
static bool CurrentProcessWasSpawnedByRegularUser();
KEXT_STATIC bool IsFileSystemCrawler(const char* procname);

static void WaitForListenerCompletion();
KEXT_STATIC bool ShouldIgnoreVnodeType(vtype vnodeType, vnode_t vnode);

KEXT_STATIC bool ShouldHandleVnodeOpEvent(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t _Nonnull context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    vtype* vnodeType,
    uint32_t* vnodeFileFlags,
    int* pid,
    char procname[MAXCOMLEN + 1],
    int* kauthResult,
    int* kauthError);

static bool TryGetVirtualizationRoot(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t _Nonnull context,
    const vnode_t vnode,
    pid_t pidMakingRequest,
    ProviderCallbackPolicy callbackPolicy,
    
    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* kauthResult,
    int* kauthError);

static bool ShouldHandleFileOpEvent(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t _Nonnull context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* pid);

// State
static kauth_listener_t s_vnodeListener = nullptr;
static kauth_listener_t s_fileopListener = nullptr;

static atomic_int s_numActiveKauthEvents;

// Public functions
kern_return_t KauthHandler_Init()
{
    if (nullptr != s_vnodeListener)
    {
        goto CleanupAndFail;
    }
    
    if (!ProviderMessaging_Init())
    {
        goto CleanupAndFail;
    }
        
    if (VirtualizationRoots_Init())
    {
        goto CleanupAndFail;
    }
    
    s_vnodeListener = kauth_listen_scope(KAUTH_SCOPE_VNODE, HandleVnodeOperation, nullptr);
    if (nullptr == s_vnodeListener)
    {
        goto CleanupAndFail;
    }
    
    s_fileopListener = kauth_listen_scope(KAUTH_SCOPE_FILEOP, HandleFileOpOperation, nullptr);
    if (nullptr == s_fileopListener)
    {
        goto CleanupAndFail;
    }
    
    return KERN_SUCCESS;
    
CleanupAndFail:
    KauthHandler_Cleanup();
    return KERN_FAILURE;
}

kern_return_t KauthHandler_Cleanup()
{
    kern_return_t result = KERN_SUCCESS;
    
    // First, stop new listener callback calls
    if (nullptr != s_vnodeListener)
    {
        kauth_unlisten_scope(s_vnodeListener);
        s_vnodeListener = nullptr;
    }
    else
    {
        result = KERN_FAILURE;
    }
    
    if (nullptr != s_fileopListener)
    {
        kauth_unlisten_scope(s_fileopListener);
        s_fileopListener = nullptr;
    }
    else
    {
        result = KERN_FAILURE;
    }

    ProviderMessaging_AbortAllOutstandingEvents();
    
    WaitForListenerCompletion();

    if (VirtualizationRoots_Cleanup())
    {
        result = KERN_FAILURE;
    }
    
    ProviderMessaging_Cleanup();
    
    return result;
}

static void UseMainForkIfNamedStream(
    // In+out params:
    vnode_t& vnode,
    // Out params:
    bool&    putVnodeWhenDone)
{
    // A lot of our file checks such as attribute tests behave oddly if the vnode
    // refers to a named fork/stream; apply the logic as if the vnode operation was
    // occurring on the file itself. (/path/to/file/..namedfork/rsrc)
    if (vnode_isnamedstream(vnode))
    {
        vnode_t mainFileFork = vnode_getparent(vnode);
        assert(NULLVP != mainFileFork);
        
        vnode = mainFileFork;
        putVnodeWhenDone = true;
    }
    else
    {
        putVnodeWhenDone = false;
    }
}

// Private functions
KEXT_STATIC int HandleVnodeOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3)
{
    atomic_fetch_add(&s_numActiveKauthEvents, 1);

    PerfTracer perfTracer;
    PerfSample vnodeOpFunctionSample(&perfTracer, PrjFSPerfCounter_VnodeOp);
    
    vfs_context_t _Nonnull context = reinterpret_cast<vfs_context_t>(arg0);
    vnode_t currentVnode =  reinterpret_cast<vnode_t>(arg1);
    // arg2 is the (vnode_t) parent vnode
    int* kauthError =       reinterpret_cast<int*>(arg3);
    int kauthResult = KAUTH_RESULT_DEFER;
    bool putVnodeWhenDone = false;

    UseMainForkIfNamedStream(currentVnode, putVnodeWhenDone);

    VirtualizationRootHandle root = RootHandle_None;
    vtype vnodeType;
    uint32_t currentVnodeFileFlags;
    FsidInode vnodeFsidInode;
    int pid = 0;
    char procname[MAXCOMLEN + 1] = "";
    bool isDeleteAction = false;
    bool isDirectory = false;

    if (!ShouldHandleVnodeOpEvent(
            &perfTracer,
            context,
            currentVnode,
            action,
            &vnodeType,
            &currentVnodeFileFlags,
            &pid,
            procname,
            &kauthResult,
            kauthError))
    {
        goto CleanupAndReturn;
    }
    
    isDeleteAction = ActionBitIsSet(action, KAUTH_VNODE_DELETE);
    isDirectory = VDIR == vnodeType;
    
    if (isDeleteAction)
    {
        // Allow any user to delete individual files, as this generally doesn't cause nested kauth callbacks.
        ProviderCallbackPolicy callbackPolicy = isDirectory ? CallbackPolicy_UserInitiatedOnly : CallbackPolicy_AllowAny;
        if (!TryGetVirtualizationRoot(&perfTracer, context, currentVnode, pid, callbackPolicy, &root, &vnodeFsidInode, &kauthResult, kauthError))
        {
            goto CleanupAndReturn;
        }
        
        PerfSample preDeleteSample(&perfTracer, PrjFSPerfCounter_VnodeOp_PreDelete);
        
        if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                root,
                isDirectory ?
                    MessageType_KtoU_NotifyDirectoryPreDelete :
                    MessageType_KtoU_NotifyFilePreDelete,
                currentVnode,
                vnodeFsidInode,
                nullptr, // path not needed, use fsid/inode
                pid,
                procname,
                &kauthResult,
                kauthError))
        {
            goto CleanupAndReturn;
        }
    }
    
    if (isDirectory)
    {
        if (ActionBitIsSet(
                action,
                KAUTH_VNODE_LIST_DIRECTORY |
                KAUTH_VNODE_SEARCH |
                KAUTH_VNODE_READ_SECURITY |
                KAUTH_VNODE_READ_ATTRIBUTES |
                KAUTH_VNODE_READ_EXTATTRIBUTES |
                KAUTH_VNODE_DELETE))
        {
            // Recursively expand directory on delete to ensure child placeholders are created before rename operations
            if (isDeleteAction)
            {
                // Prevent system services from expanding directories as part of enumeration as this tends to cause deadlocks with the kauth listeners for Antivirus software
                if (!TryGetVirtualizationRoot(&perfTracer, context, currentVnode, pid, CallbackPolicy_UserInitiatedOnly, &root, &vnodeFsidInode, &kauthResult, kauthError))
                {
                    goto CleanupAndReturn;
                }

                PerfSample recursivelyEnumerateSample(&perfTracer, PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory);
        
                if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                        root,
                        MessageType_KtoU_RecursivelyEnumerateDirectory,
                        currentVnode,
                        vnodeFsidInode,
                        nullptr, // path not needed, use fsid/inode
                        pid,
                        procname,
                        &kauthResult,
                        kauthError))
                {
                    goto CleanupAndReturn;
                }
            }
            else if (FileFlagsBitIsSet(currentVnodeFileFlags, FileFlags_IsEmpty))
            {
                // Prevent system services from expanding directories as part of enumeration as this tends to cause deadlocks with the kauth listeners for Antivirus software
                if (!TryGetVirtualizationRoot(&perfTracer, context, currentVnode, pid, CallbackPolicy_UserInitiatedOnly, &root, &vnodeFsidInode, &kauthResult, kauthError))
                {
                    goto CleanupAndReturn;
                }

                PerfSample enumerateDirectorySample(&perfTracer, PrjFSPerfCounter_VnodeOp_EnumerateDirectory);
        
                if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                        root,
                        MessageType_KtoU_EnumerateDirectory,
                        currentVnode,
                        vnodeFsidInode,
                        nullptr, // path not needed, use fsid/inode
                        pid,
                        procname,
                        &kauthResult,
                        kauthError))
                {
                    goto CleanupAndReturn;
                }
            }
        }
    }
    else
    {
        if (ActionBitIsSet(
                action,
                KAUTH_VNODE_READ_ATTRIBUTES |
                KAUTH_VNODE_WRITE_ATTRIBUTES |
                KAUTH_VNODE_READ_EXTATTRIBUTES |
                KAUTH_VNODE_WRITE_EXTATTRIBUTES |
                KAUTH_VNODE_READ_DATA |
                KAUTH_VNODE_WRITE_DATA |
                KAUTH_VNODE_EXECUTE |
                KAUTH_VNODE_DELETE)) // Hydrate on delete to ensure files are hydrated before rename operations
        {
            if (FileFlagsBitIsSet(currentVnodeFileFlags, FileFlags_IsEmpty))
            {
                // Prevent system services from hydrating files as this tends to cause deadlocks with the kauth listeners for Antivirus software
                if (!TryGetVirtualizationRoot(&perfTracer, context, currentVnode, pid, CallbackPolicy_UserInitiatedOnly, &root, &vnodeFsidInode, &kauthResult, kauthError))
                {
                    goto CleanupAndReturn;
                }

                PerfSample enumerateDirectorySample(&perfTracer, PrjFSPerfCounter_VnodeOp_HydrateFile);

                if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                        root,
                        MessageType_KtoU_HydrateFile,
                        currentVnode,
                        vnodeFsidInode,
                        nullptr, // path not needed, use fsid/inode
                        pid,
                        procname,
                        &kauthResult,
                        kauthError))
                {
                    goto CleanupAndReturn;
                }
            }
            
            if (ActionBitIsSet(action, KAUTH_VNODE_WRITE_DATA))
            {
                if (!TryGetVirtualizationRoot(&perfTracer, context, currentVnode, pid, CallbackPolicy_UserInitiatedOnly, &root, &vnodeFsidInode, &kauthResult, kauthError))
                {
                    goto CleanupAndReturn;
                }
                
                PrjFSFileXAttrData rootXattr = {};
                SizeOrError xattrResult = Vnode_ReadXattr(currentVnode, PrjFSFileXAttrName, &rootXattr, sizeof(rootXattr));
                if (xattrResult.error == ENOATTR)
                {
                    // Only notify if the file is still a placeholder
                    goto CleanupAndReturn;
                }
                
                PerfSample preConvertToFullSample(&perfTracer, PrjFSPerfCounter_VnodeOp_PreConvertToFull);
                
                if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                                                      root,
                                                      MessageType_KtoU_NotifyFilePreConvertToFull,
                                                      currentVnode,
                                                      vnodeFsidInode,
                                                      nullptr, // path not needed, use fsid/inode,
                                                      pid,
                                                      procname,
                                                      &kauthResult,
                                                      kauthError))
                {
                    goto CleanupAndReturn;
                }
            }
        }
    }
    
CleanupAndReturn:
    if (putVnodeWhenDone)
    {
        vnode_put(currentVnode);
    }
    
    atomic_fetch_sub(&s_numActiveKauthEvents, 1);
    return kauthResult;
}

// Note: a fileop listener MUST NOT return an error, or it will result in a kernel panic.
// Fileop events are informational only.
KEXT_STATIC int HandleFileOpOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3)
{
    atomic_fetch_add(&s_numActiveKauthEvents, 1);
    
    PerfTracer perfTracer;
    PerfSample fileOpSample(&perfTracer, PrjFSPerfCounter_FileOp);

    vfs_context_t _Nonnull context = vfs_context_create(NULL);
    vnode_t currentVnode = NULLVP;
    bool    putCurrentVnode = false;

    if (KAUTH_FILEOP_RENAME == action ||
        KAUTH_FILEOP_LINK == action)
    {
        // arg0 is the (const char *) fromPath (or the file being linked to)
        const char* newPath = reinterpret_cast<const char*>(arg1);
        
        // TODO(Mac): We need to handle failures to lookup the vnode.  If we fail to lookup the vnode
        // it's possible that we'll miss notifications
        errno_t toErr = vnode_lookup(newPath, 0 /* flags */, &currentVnode, context);
        if (0 != toErr)
        {
            KextLog_Error("HandleFileOpOperation: vnode_lookup failed, errno %d for path '%s'", toErr, newPath);
            goto CleanupAndReturn;
        }
        
        // Don't expect named stream here as they can't be directly hardlinked or renamed, only the main fork can
        assert(!vnode_isnamedstream(currentVnode));
        
        putCurrentVnode = true;
        
        VirtualizationRootHandle root = RootHandle_None;
        FsidInode vnodeFsidInode = {};
        int pid;
        if (!ShouldHandleFileOpEvent(
                &perfTracer,
                context,
                currentVnode,
                action,
                &root,
                &vnodeFsidInode,
                &pid))
        {
            goto CleanupAndReturn;
        }
        
        char procname[MAXCOMLEN + 1];
        proc_name(pid, procname, MAXCOMLEN + 1);

        if (KAUTH_FILEOP_RENAME == action)
        {
            PerfSample renameSample(&perfTracer, PrjFSPerfCounter_FileOp_Renamed);
            
            MessageType messageType =
                vnode_isdir(currentVnode)
                ? MessageType_KtoU_NotifyDirectoryRenamed
                : MessageType_KtoU_NotifyFileRenamed;

            int kauthResult;
            int kauthError;
            if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                    root,
                    messageType,
                    currentVnode,
                    vnodeFsidInode,
                    newPath,
                    pid,
                    procname,
                    &kauthResult,
                    &kauthError))
            {
                goto CleanupAndReturn;
            }
        }
        else
        {
            PerfSample hardLinkSample(&perfTracer, PrjFSPerfCounter_FileOp_HardLinkCreated);
        
            int kauthResult;
            int kauthError;
            if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                    root,
                    MessageType_KtoU_NotifyFileHardLinkCreated,
                    currentVnode,
                    vnodeFsidInode,
                    newPath,
                    pid,
                    procname,
                    &kauthResult,
                    &kauthError))
            {
                goto CleanupAndReturn;
            }
        }
    }
    else if (KAUTH_FILEOP_OPEN == action)
    {
        currentVnode = reinterpret_cast<vnode_t>(arg0);
        putCurrentVnode = false;
        const char* path = reinterpret_cast<const char*>(arg1);

        if (vnode_isdir(currentVnode))
        {
            goto CleanupAndReturn;
        }

        UseMainForkIfNamedStream(currentVnode, putCurrentVnode);

        bool fileFlaggedInRoot;
        if (!TryGetFileIsFlaggedAsInRoot(currentVnode, context, &fileFlaggedInRoot))
        {
            KextLog_ErrorVnodeProperties(currentVnode, "KAUTH_FILEOP_OPEN: checking file flags failed. Path = '%s'", path);
            
            goto CleanupAndReturn;
        }
        
        if (fileFlaggedInRoot)
        {
            goto CleanupAndReturn;
        }
        
        VirtualizationRootHandle root = RootHandle_None;
        FsidInode vnodeFsidInode;
        int pid;
        if (!ShouldHandleFileOpEvent(
                                     &perfTracer,
                                     context,
                                     currentVnode,
                                     action,
                                     &root,
                                     &vnodeFsidInode,
                                     &pid))
        {
            goto CleanupAndReturn;
        }
        
        char procname[MAXCOMLEN + 1];
        proc_name(pid, procname, MAXCOMLEN + 1);
        PerfSample fileCreatedSample(&perfTracer, PrjFSPerfCounter_FileOp_FileCreated);
        int kauthResult;
        int kauthError;
        if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                                              root,
                                              MessageType_KtoU_NotifyFileCreated,
                                              currentVnode,
                                              vnodeFsidInode,
                                              path,
                                              pid,
                                              procname,
                                              &kauthResult,
                                              &kauthError))
        {
            goto CleanupAndReturn;
        }
    }
    else if (KAUTH_FILEOP_CLOSE == action)
    {
        currentVnode = reinterpret_cast<vnode_t>(arg0);
        putCurrentVnode = false;
        const char* path = reinterpret_cast<const char*>(arg1);
        int closeFlags = static_cast<int>(arg2);
        
        if (vnode_isdir(currentVnode))
        {
            goto CleanupAndReturn;
        }
        
        UseMainForkIfNamedStream(currentVnode, putCurrentVnode);
        
        if (KAUTH_FILEOP_CLOSE_MODIFIED != closeFlags)
        {
            goto CleanupAndReturn;
        }
            
        VirtualizationRootHandle root = RootHandle_None;
        FsidInode vnodeFsidInode = {};
        int pid;
        if (!ShouldHandleFileOpEvent(
                &perfTracer,
                context,
                currentVnode,
                action,
                &root,
                &vnodeFsidInode,
                &pid))
        {
            goto CleanupAndReturn;
        }

        char procname[MAXCOMLEN + 1];
        proc_name(pid, procname, MAXCOMLEN + 1);
        PerfSample fileModifiedSample(&perfTracer, PrjFSPerfCounter_FileOp_FileModified);
        int kauthResult;
        int kauthError;
        if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
                root,
                MessageType_KtoU_NotifyFileModified,
                currentVnode,
                vnodeFsidInode,
                path,
                pid,
                procname,
                &kauthResult,
                &kauthError))
        {
            goto CleanupAndReturn;
        }
    }
    
CleanupAndReturn:    
    if (NULLVP != currentVnode && putCurrentVnode)
    {
        vnode_put(currentVnode);
    }
    
    vfs_context_rele(context);
    atomic_fetch_sub(&s_numActiveKauthEvents, 1);
    
    // We must always return DEFER from a fileop listener. The kernel does not allow any other
    // result and will panic if we return anything else.
    return KAUTH_RESULT_DEFER;
}

KEXT_STATIC bool ShouldHandleVnodeOpEvent(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t _Nonnull context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    vtype* vnodeType,
    uint32_t* vnodeFileFlags,
    int* pid,
    char procname[MAXCOMLEN + 1],
    int* kauthResult,
    int* kauthError)
{
    PerfSample handleVnodeSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle);

    *kauthResult = KAUTH_RESULT_DEFER;
    
    {
        PerfSample isVnodeAccessSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle_IsVnodeAccessCheck);
        
        if (ActionBitIsSet(action, KAUTH_VNODE_ACCESS))
        {
            perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeAccessCheck);
            
            // From kauth.h:
            //    "The KAUTH_VNODE_ACCESS bit is passed to the callback if the authorisation
            //    request in progress is advisory, rather than authoritative.  Listeners
            //    performing consequential work (i.e. not strictly checking authorisation)
            //    may test this flag to avoid performing unnecessary work."
            *kauthResult = KAUTH_RESULT_DEFER;
            return false;
        }
    }
    
    {
        PerfSample isAllowedSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle_IsAllowedFileSystem);
        if (!VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode))
        {
            *kauthResult = KAUTH_RESULT_DEFER;
            return false;
        }
    }

    {
        PerfSample shouldIgnoreSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle_ShouldIgnoreVnodeType);
        
        *vnodeType = vnode_vtype(vnode);
        if (ShouldIgnoreVnodeType(*vnodeType, vnode))
        {
            perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeType);
        
            *kauthResult = KAUTH_RESULT_DEFER;
            return false;
        }
    }
    
    {
        PerfSample readFlagsSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle_ReadFileFlags);
        if (!TryReadVNodeFileFlags(vnode, context, vnodeFileFlags))
        {
            *kauthError = EBADF;
            *kauthResult = KAUTH_RESULT_DENY;
            return false;
        }

        if (!FileFlagsBitIsSet(*vnodeFileFlags, FileFlags_IsInVirtualizationRoot))
        {
            // This vnode is not part of ANY virtualization root, so exit now before doing any more work.
            // This gives us a cheap way to avoid adding overhead to IO outside of a virtualization root.
            
            perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_ShouldHandle_NotInAnyRoot);

            *kauthResult = KAUTH_RESULT_DEFER;
            return false;
        }
    }

    *pid = GetPid(context);
    proc_name(*pid, procname, MAXCOMLEN + 1);
    
    if (FileFlagsBitIsSet(*vnodeFileFlags, FileFlags_IsEmpty))
    {
        // This vnode is not yet hydrated, so do not allow a file system crawler to force hydration.
        // Once a vnode is hydrated, it's fine to allow crawlers to access those contents.
        
        PerfSample crawlerSample(perfTracer, PrjFSPerfCounter_VnodeOp_ShouldHandle_CheckFileSystemCrawler);
        if (IsFileSystemCrawler(procname))
        {
            // We must DENY file system crawlers rather than DEFER.
            // If we allow the crawler's access to succeed without hydrating, the kauth result will be cached and we won't
            // get called again, so we lose the opportunity to hydrate the file/directory and it will appear as though
            // it is missing its contents.

            perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_ShouldHandle_DeniedFileSystemCrawler);
            
            *kauthResult = KAUTH_RESULT_DENY;
            return false;
        }
    }
    
    return true;
}

static bool TryGetVirtualizationRoot(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t _Nonnull context,
    const vnode_t vnode,
    pid_t pidMakingRequest,
    ProviderCallbackPolicy callbackPolicy,

    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* kauthResult,
    int* kauthError)
{
    PerfSample findRootSample(perfTracer, PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot);
        
    *vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context, true /* the inode is used for getting the path in the provider, so use linkid */);
    *root = VirtualizationRoot_FindForVnode(
        perfTracer,
        PrjFSPerfCounter_VnodeOp_FindRoot,
        PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
        vnode,
        context);

    if (RootHandle_ProviderTemporaryDirectory == *root)
    {
        perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_TemporaryDirectory);
    
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    else if (RootHandle_None == *root)
    {
        KextLog_File(vnode, "No virtualization root found for file with set flag.");
        perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_NoRootFound);
    
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    ActiveProviderProperties provider = VirtualizationRoot_GetActiveProvider(*root);
    if (!provider.isOnline)
    {
        // TODO(Mac): Protect files in the worktree from modification (and prevent
        // the creation of new files) when the provider is offline
        
        perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_ProviderOffline);
        
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    if (provider.pid == pidMakingRequest)
    {
        // If the calling process is the provider, we must exit right away to avoid deadlocks
        perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_OriginatedByProvider);
        
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    if (callbackPolicy == CallbackPolicy_UserInitiatedOnly && !CurrentProcessWasSpawnedByRegularUser())
    {
        // Prevent hydration etc. by system services
        KextLog_Info("TryGetVirtualizationRoot: process %d restricted due to owner UID.", pidMakingRequest);
        perfTracer->IncrementCount(PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_UserRestriction);
        
        *kauthResult = KAUTH_RESULT_DENY;
        return false;
    }
    
    return true;
}


static bool CurrentProcessWasSpawnedByRegularUser()
{
    bool nonServiceUser = false;
    
    proc_t process = proc_self();
    
    while (true)
    {
        kauth_cred_t credential = kauth_cred_proc_ref(process);
        uid_t processUID = kauth_cred_getuid(credential);
        kauth_cred_unref(&credential);
        
        if (processUID >= 500)
        {
            nonServiceUser = true;
            break;
        }
        
        pid_t parentPID = proc_ppid(process);
        if (parentPID <= 1)
        {
            break;
        }
        
        proc_t parentProcess = proc_find(parentPID);
        proc_rele(process);
        process = parentProcess;
        if (parentProcess == nullptr)
        {
            KextLog_Error("CurrentProcessIsOwnedOrWasSpawnedByRegularUser: Failed to locate ancestor process %d for current process %d\n", parentPID, proc_selfpid());
            break;
        }
    }
    
    
    if (process != nullptr)
    {
        proc_rele(process);
    }
    
    return nonServiceUser;
}
    

static bool ShouldHandleFileOpEvent(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* pid)
{
    PerfSample fileOpSample(perfTracer, PrjFSPerfCounter_FileOp_ShouldHandle);

    *root = RootHandle_None;

    if (!VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode))
    {
        return false;
    }

    vtype vnodeType = vnode_vtype(vnode);
    if (ShouldIgnoreVnodeType(vnodeType, vnode))
    {
        return false;
    }
    
    {
        PerfSample findRootSample(perfTracer, PrjFSPerfCounter_FileOp_ShouldHandle_FindVirtualizationRoot);
        
        *root = VirtualizationRoot_FindForVnode(
            perfTracer,
            PrjFSPerfCounter_FileOp_FindRoot,
            PrjFSPerfCounter_FileOp_FindRoot_Iteration,
            vnode,
            context);
        
        if (!VirtualizationRoot_IsValidRootHandle(*root))
        {
            perfTracer->IncrementCount(PrjFSPerfCounter_FileOp_ShouldHandle_NoRootFound);
            return false;
        }
    }
    
    {
        PerfSample checkRootOnlineSample(perfTracer, PrjFSPerfCounter_FileOp_ShouldHandle_CheckProvider);
        
        ActiveProviderProperties provider = VirtualizationRoot_GetActiveProvider(*root);
        
        if (!provider.isOnline)
        {
            perfTracer->IncrementCount(PrjFSPerfCounter_FileOp_ShouldHandle_OfflineRoot);
            return false;
        }
    
        // If the calling process is the provider, we must exit right away to avoid deadlocks
        *pid = GetPid(context);
        if (*pid == provider.pid)
        {
            perfTracer->IncrementCount(PrjFSPerfCounter_FileOp_ShouldHandle_OriginatedByProvider);
            return false;
        }
    }

    *vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context, true /* the inode is used for getting the path in the provider, so use linkid */);

    return true;
}

static void WaitForListenerCompletion()
{
    // Wait until all kauth events have noticed and returned.
    // Always sleeping at least once reduces the likelihood of a race condition
    // between kauth_unlisten_scope and the s_numActiveKauthEvents increment at
    // the start of the callback.
    // This race condition and the inability to work around it is a longstanding
    // bug in the xnu kernel - see comment block in RemoveListener() function of
    // the KauthORama sample code:
    // https://developer.apple.com/library/archive/samplecode/KauthORama/Listings/KauthORama_c.html#//apple_ref/doc/uid/DTS10003633-KauthORama_c-DontLinkElementID_3
    do
    {
        Mutex_Sleep(1, nullptr, nullptr);
    } while (atomic_load(&s_numActiveKauthEvents) > 0);
}

static int GetPid(vfs_context_t _Nonnull context)
{
    proc_t callingProcess = vfs_context_proc(context);
    return proc_pid(callingProcess);
}

static errno_t GetVNodeAttributes(vnode_t vn, vfs_context_t _Nonnull context, struct vnode_attr* attrs)
{
    VATTR_INIT(attrs);
    VATTR_WANTED(attrs, va_flags);
    
    return vnode_getattr(vn, attrs, context);
}

static bool TryReadVNodeFileFlags(vnode_t vn, vfs_context_t _Nonnull context, uint32_t* flags)
{
    struct vnode_attr attributes = {};
    *flags = 0;
    errno_t err = GetVNodeAttributes(vn, context, &attributes);
    if (0 != err)
    {
        // TODO(Mac): May fail on some file system types? Perhaps we should early-out depending on mount point anyway.
        // We should also consider:
        //   - Logging this error
        //   - Falling back on vnode lookup (or custom cache) to determine if file is in the root
        //   - Assuming files are empty if we can't read the flags
        KextLog_FileError(vn, "ReadVNodeFileFlags: GetVNodeAttributes failed with error %d; vnode type: %d, recycled: %s", err, vnode_vtype(vn), vnode_isrecycled(vn) ? "yes" : "no");
        return false;
    }
    
    assert(VATTR_IS_SUPPORTED(&attributes, va_flags));
    *flags = attributes.va_flags;
    return true;
}

KEXT_STATIC_INLINE bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit)
{
    // Note: if multiple bits are set in 'bit', this will return true if ANY are set in fileFlags
    return 0 != (fileFlags & bit);
}

KEXT_STATIC_INLINE bool TryGetFileIsFlaggedAsInRoot(vnode_t vnode, vfs_context_t _Nonnull context, bool* flaggedInRoot)
{
    uint32_t vnodeFileFlags;
    *flaggedInRoot = false;
    if (!TryReadVNodeFileFlags(vnode, context, &vnodeFileFlags))
    {
        return false;
    }
    
    *flaggedInRoot = FileFlagsBitIsSet(vnodeFileFlags, FileFlags_IsInVirtualizationRoot);
    return true;
}

KEXT_STATIC_INLINE bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask)
{
    return action & mask;
}

KEXT_STATIC bool IsFileSystemCrawler(const char* procname)
{
    // These process will crawl the file system and force a full hydration
    if (!strcmp(procname, "mds") ||
        !strcmp(procname, "mdworker") ||
        !strcmp(procname, "mds_stores") ||
        !strcmp(procname, "fseventsd") ||
        !strcmp(procname, "Spotlight"))
    {
        return true;
    }
    
    return false;
}

KEXT_STATIC bool ShouldIgnoreVnodeType(vtype vnodeType, vnode_t vnode)
{
    switch (vnodeType)
    {
    case VNON:
    case VBLK:
    case VCHR:
    case VSOCK:
    case VFIFO:
    case VBAD:
        return true;
	case VREG:
    case VDIR:
    case VLNK:
        return false;
    case VSTR:
    case VCPLX:
        KextLog_FileInfo(vnode, "vnode with type %s encountered", vnodeType == VSTR ? "VSTR" : "VCPLX");
        return false;
    default:
        KextLog_FileInfo(vnode, "vnode with unknown type %d encountered", vnodeType);
        return false;
    }
}
