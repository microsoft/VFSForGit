#include <kern/debug.h>
#include <sys/kauth.h>
#include <sys/proc.h>
#include <libkern/OSAtomic.h>
#include <kern/assert.h>
#include <stdatomic.h>

#include "PrjFSCommon.h"
#include "VirtualizationRoots.hpp"
#include "VnodeUtilities.hpp"
#include "KauthHandler.hpp"
#include "KextLog.hpp"
#include "Message.h"
#include "Locks.hpp"
#include "PrjFSProviderUserClient.hpp"
#include "PerformanceTracing.hpp"

// Function prototypes
static int HandleVnodeOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3);

static int HandleFileOpOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3);

static int GetPid(vfs_context_t context);

static uint32_t ReadVNodeFileFlags(vnode_t vn, vfs_context_t context);
static inline bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit);
static inline bool FileIsFlaggedAsInRoot(vnode_t vnode, vfs_context_t context);
static inline bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask);

static bool IsFileSystemCrawler(char* procname);

static void Sleep(int seconds, void* channel);
static bool TrySendRequestAndWaitForResponse(
    VirtualizationRootHandle root,
    MessageType messageType,
    const vnode_t vnode,
    const FsidInode& vnodeFsidInode,
    const char* vnodePath,
    int pid,
    const char* procname,
    int* kauthResult,
    int* kauthError);
static void AbortAllOutstandingEvents();
static bool ShouldIgnoreVnodeType(vtype vnodeType, vnode_t vnode);

static bool ShouldHandleVnodeOpEvent(
    // In params:
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,
    ProfileSample& operationSample,

    // Out params:
    VirtualizationRootHandle* root,
    vtype* vnodeType,
    uint32_t* vnodeFileFlags,
    FsidInode* vnodeFsidInode,
    int* pid,
    char procname[MAXCOMLEN + 1],
    int* kauthResult);

static bool ShouldHandleFileOpEvent(
    // In params:
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* pid);

// Structs
typedef struct OutstandingMessage
{
    MessageHeader request;
    MessageType response;
    bool    receivedResponse;
    
    LIST_ENTRY(OutstandingMessage) _list_privates;
    
} OutstandingMessage;

// State
static kauth_listener_t s_vnodeListener = nullptr;
static kauth_listener_t s_fileopListener = nullptr;

static LIST_HEAD(OutstandingMessage_Head, OutstandingMessage) s_outstandingMessages = LIST_HEAD_INITIALIZER(OutstandingMessage_Head);
static Mutex s_outstandingMessagesMutex = {};
static volatile int s_nextMessageId;

static atomic_int s_numActiveKauthEvents;
static volatile bool s_isShuttingDown;

// Public functions
kern_return_t KauthHandler_Init()
{
    if (nullptr != s_vnodeListener)
    {
        goto CleanupAndFail;
    }
    
    LIST_INIT(&s_outstandingMessages);
    s_nextMessageId = 1;
    
    s_isShuttingDown = false;
    
    s_outstandingMessagesMutex = Mutex_Alloc();
    if (!Mutex_IsValid(s_outstandingMessagesMutex))
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

    // Then, ensure there are no more callbacks in flight.
    AbortAllOutstandingEvents();

    if (VirtualizationRoots_Cleanup())
    {
        result = KERN_FAILURE;
    }
        
    if (Mutex_IsValid(s_outstandingMessagesMutex))
    {
        Mutex_FreeMemory(&s_outstandingMessagesMutex);
    }
    else
    {
        result = KERN_FAILURE;
    }
    
    return result;
}

void KauthHandler_HandleKernelMessageResponse(uint64_t messageId, MessageType responseType)
{
    switch (responseType)
    {
        case MessageType_Response_Success:
        case MessageType_Response_Fail:
        {
            Mutex_Acquire(s_outstandingMessagesMutex);
            {
                OutstandingMessage* outstandingMessage;
                LIST_FOREACH(outstandingMessage, &s_outstandingMessages, _list_privates)
                {
                    if (outstandingMessage->request.messageId == messageId)
                    {
                        // Save the response for the blocked thread.
                        outstandingMessage->response = responseType;
                        outstandingMessage->receivedResponse = true;
                        
                        wakeup(outstandingMessage);
                        
                        break;
                    }
                }
            }
            Mutex_Release(s_outstandingMessagesMutex);
            break;
        }
        
        // The follow are not valid responses to kernel messages
        case MessageType_Invalid:
        case MessageType_UtoK_StartVirtualizationInstance:
        case MessageType_UtoK_StopVirtualizationInstance:
        case MessageType_KtoU_EnumerateDirectory:
        case MessageType_KtoU_RecursivelyEnumerateDirectory:
        case MessageType_KtoU_HydrateFile:
        case MessageType_KtoU_NotifyFileModified:
        case MessageType_KtoU_NotifyFilePreDelete:
        case MessageType_KtoU_NotifyDirectoryPreDelete:
        case MessageType_KtoU_NotifyFileCreated:
        case MessageType_KtoU_NotifyFileRenamed:
        case MessageType_KtoU_NotifyDirectoryRenamed:
        case MessageType_KtoU_NotifyFileHardLinkCreated:
            KextLog_Error("KauthHandler_HandleKernelMessageResponse: Unexpected responseType: %d", responseType);
            break;
    }
    
    return;
}

// Private functions
static int HandleVnodeOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3)
{
    atomic_fetch_add(&s_numActiveKauthEvents, 1);

    ProfileSample functionSample(Probe_VnodeOp);
    
    vfs_context_t context = reinterpret_cast<vfs_context_t>(arg0);
    vnode_t currentVnode =  reinterpret_cast<vnode_t>(arg1);
    // arg2 is the (vnode_t) parent vnode
    int* kauthError =       reinterpret_cast<int*>(arg3);
    int kauthResult = KAUTH_RESULT_DEFER;
    bool putVnodeWhenDone = false;

    // A lot of our file checks such as attribute tests behave oddly if the vnode
    // refers to a named fork/stream; apply the logic as if the vnode operation was
    // occurring on the file itself. (/path/to/file/..namedfork/rsrc)
    if (vnode_isnamedstream(currentVnode))
    {
        vnode_t mainFileFork = vnode_getparent(currentVnode);
        assert(NULLVP != mainFileFork);
        currentVnode = mainFileFork;
        putVnodeWhenDone = true;
    }

    const char* vnodePath = nullptr;
    char vnodePathBuffer[PrjFSMaxPath];
    int vnodePathLength = PrjFSMaxPath;

    VirtualizationRootHandle root = RootHandle_None;
    vtype vnodeType;
    uint32_t currentVnodeFileFlags;
    FsidInode vnodeFsidInode;
    int pid;
    char procname[MAXCOMLEN + 1];
    bool isDeleteAction = false;
    bool isDirectory = false;
    
    // TODO(Mac): Issue #271 - Reduce reliance on vn_getpath
    // Call vn_getpath first when the cache is hottest to increase the chances
    // of successfully getting the path
    if (0 == vn_getpath(currentVnode, vnodePathBuffer, &vnodePathLength))
    {
        vnodePath = vnodePathBuffer;
    }

    if (!ShouldHandleVnodeOpEvent(
            context,
            currentVnode,
            action,
            functionSample,
            &root,
            &vnodeType,
            &currentVnodeFileFlags,
            &vnodeFsidInode,
            &pid,
            procname,
            &kauthResult))
    {
        goto CleanupAndReturn;
    }
    
    isDeleteAction = ActionBitIsSet(action, KAUTH_VNODE_DELETE);
    isDirectory = VDIR == vnodeType;
    
    if (isDeleteAction)
    {
        if (!TrySendRequestAndWaitForResponse(
                root,
                isDirectory ?
                    MessageType_KtoU_NotifyDirectoryPreDelete :
                    MessageType_KtoU_NotifyFilePreDelete,
                currentVnode,
                vnodeFsidInode,
                vnodePath,
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
            if (isDeleteAction || FileFlagsBitIsSet(currentVnodeFileFlags, FileFlags_IsEmpty))
            {
                functionSample.SetProbe(Probe_VnodeOp_PopulatePlaceholderDirectory);

                if (!TrySendRequestAndWaitForResponse(
                        root,
                        isDeleteAction ?
                            MessageType_KtoU_RecursivelyEnumerateDirectory :
                            MessageType_KtoU_EnumerateDirectory,
                        currentVnode,
                        vnodeFsidInode,
                        vnodePath,
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
                functionSample.SetProbe(Probe_VnodeOp_HydratePlaceholderFile);

                if (!TrySendRequestAndWaitForResponse(
                        root,
                        MessageType_KtoU_HydrateFile,
                        currentVnode,
                        vnodeFsidInode,
                        vnodePath,
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
static int HandleFileOpOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3)
{
    atomic_fetch_add(&s_numActiveKauthEvents, 1);
    
    ProfileSample functionSample(Probe_FileOp);

    vfs_context_t context = vfs_context_create(NULL);
    vnode_t currentVnodeFromPath = NULLVP;

    if (KAUTH_FILEOP_RENAME == action ||
        KAUTH_FILEOP_LINK == action)
    {
        // arg0 is the (const char *) fromPath (or the file being linked to)
        const char* newPath = reinterpret_cast<const char*>(arg1);
        
        // TODO(Mac): We need to handle failures to lookup the vnode.  If we fail to lookup the vnode
        // it's possible that we'll miss notifications
        errno_t toErr = vnode_lookup(newPath, 0 /* flags */, &currentVnodeFromPath, context);
        if (0 != toErr)
        {
            goto CleanupAndReturn;
        }
        
        VirtualizationRootHandle root = RootHandle_None;
        FsidInode vnodeFsidInode;
        int pid;
        if (!ShouldHandleFileOpEvent(
                context,
                currentVnodeFromPath,
                action,
                &root,
                &vnodeFsidInode,
                &pid))
        {
            goto CleanupAndReturn;
        }
        
        char procname[MAXCOMLEN + 1];
        proc_name(pid, procname, MAXCOMLEN + 1);

        MessageType messageType;
        if (KAUTH_FILEOP_RENAME == action)
        {
            messageType = vnode_isdir(currentVnodeFromPath) ? MessageType_KtoU_NotifyDirectoryRenamed : MessageType_KtoU_NotifyFileRenamed;
        }
        else
        {
            messageType = MessageType_KtoU_NotifyFileHardLinkCreated;
        }

        int kauthResult;
        int kauthError;
        if (!TrySendRequestAndWaitForResponse(
                root,
                messageType,
                currentVnodeFromPath,
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
    else if (KAUTH_FILEOP_CLOSE == action)
    {
        vnode_t currentVnode = reinterpret_cast<vnode_t>(arg0);
        const char* path = reinterpret_cast<const char*>(arg1);
        int closeFlags = static_cast<int>(arg2);
        
        if (vnode_isdir(currentVnode))
        {
            goto CleanupAndReturn;
        }

        bool fileFlaggedInRoot = FileIsFlaggedAsInRoot(currentVnode, context);
        if (fileFlaggedInRoot && KAUTH_FILEOP_CLOSE_MODIFIED != closeFlags)
        {
            goto CleanupAndReturn;
        }
            
        VirtualizationRootHandle root = RootHandle_None;
        FsidInode vnodeFsidInode;
        int pid;
        if (!ShouldHandleFileOpEvent(
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
        
        if (fileFlaggedInRoot)
        {
            int kauthResult;
            int kauthError;
            if (!TrySendRequestAndWaitForResponse(
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
        else
        {
            int kauthResult;
            int kauthError;
            if (!TrySendRequestAndWaitForResponse(
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
    }
    
CleanupAndReturn:    
    if (NULLVP != currentVnodeFromPath)
    {
        vnode_put(currentVnodeFromPath);
    }
    
    vfs_context_rele(context);
    atomic_fetch_sub(&s_numActiveKauthEvents, 1);
    
    // We must always return DEFER from a fileop listener. The kernel does not allow any other
    // result and will panic if we return anything else.
    return KAUTH_RESULT_DEFER;
}

static bool ShouldHandleVnodeOpEvent(
    // In params:
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,
    ProfileSample& operationSample,

    // Out params:
    VirtualizationRootHandle* root,
    vtype* vnodeType,
    uint32_t* vnodeFileFlags,
    FsidInode* vnodeFsidInode,
    int* pid,
    char procname[MAXCOMLEN + 1],
    int* kauthResult)
{
    *kauthResult = KAUTH_RESULT_DEFER;
    *root = RootHandle_None;
    
    if (!VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode))
    {
        operationSample.SetProbe(Probe_Op_EarlyOut);
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    *vnodeType = vnode_vtype(vnode);
    if (ShouldIgnoreVnodeType(*vnodeType, vnode))
    {
        operationSample.SetProbe(Probe_Op_EarlyOut);
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    {
        ProfileSample readflags(Probe_ReadFileFlags);
        *vnodeFileFlags = ReadVNodeFileFlags(vnode, context);
    }

    if (!FileFlagsBitIsSet(*vnodeFileFlags, FileFlags_IsInVirtualizationRoot))
    {
        // This vnode is not part of ANY virtualization root, so exit now before doing any more work.
        // This gives us a cheap way to avoid adding overhead to IO outside of a virtualization root.
        
        operationSample.SetProbe(Probe_Op_NoVirtualizationRootFlag);
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    *pid = GetPid(context);
    proc_name(*pid, procname, MAXCOMLEN + 1);
    
    if (FileFlagsBitIsSet(*vnodeFileFlags, FileFlags_IsEmpty))
    {
        // This vnode is not yet hydrated, so do not allow a file system crawler to force hydration.
        // Once a vnode is hydrated, it's fine to allow crawlers to access those contents.
        
        if (IsFileSystemCrawler(procname))
        {
            // We must DENY file system crawlers rather than DEFER.
            // If we allow the crawler's access to succeed without hydrating, the kauth result will be cached and we won't
            // get called again, so we lose the opportunity to hydrate the file/directory and it will appear as though
            // it is missing its contents.
            
            operationSample.SetProbe(Probe_Op_DenyCrawler);
            *kauthResult = KAUTH_RESULT_DENY;
            return false;
        }

        operationSample.SetProbe(Probe_Op_EmptyFlag);
    }
    
    operationSample.TakeSplitSample(Probe_Op_IdentifySplit);

    *vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context);
    *root = VirtualizationRoot_FindForVnode(vnode, *vnodeFsidInode);
    
    operationSample.TakeSplitSample(Probe_Op_VirtualizationRootFindSplit);

    if (RootHandle_ProviderTemporaryDirectory == *root)
    {
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    else if (RootHandle_None == *root)
    {
        KextLog_FileNote(vnode, "No virtualization root found for file with set flag.");
        
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    else if (!VirtualizationRoot_IsOnline(*root))
    {
        // TODO(Mac): Protect files in the worktree from modification (and prevent
        // the creation of new files) when the provider is offline
        
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    // If the calling process is the provider, we must exit right away to avoid deadlocks
    if (VirtualizationRoot_PIDMatchesProvider(*root, *pid))
    {
        operationSample.SetProbe(Probe_Op_Provider);
        *kauthResult = KAUTH_RESULT_DEFER;
        return false;
    }
    
    return true;
}

static bool ShouldHandleFileOpEvent(
    // In params:
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    VirtualizationRootHandle* root,
    FsidInode* vnodeFsidInode,
    int* pid)
{
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
    
    *vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context);
    *root = VirtualizationRoot_FindForVnode(vnode, *vnodeFsidInode);
    if (!VirtualizationRoot_IsValidRootHandle(*root))
    {
        // This VNode is not part of a root
        return false;
    }
    
    *pid = GetPid(context);
    if (VirtualizationRoot_PIDMatchesProvider(*root, *pid))
    {
        // If the calling process is the provider, we must exit right away to avoid deadlocks
        return false;
    }
    
    return true;
}

static bool TrySendRequestAndWaitForResponse(
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
    bool result = false;
    
    OutstandingMessage message;
    message.receivedResponse = false;
    
    if (nullptr == vnodePath)
    {
        // Default error code is EACCES. See errno.h for more codes.
        *kauthError = EAGAIN;
        *kauthResult = KAUTH_RESULT_DENY;
        return false;
    }
    
    const char* relativePath = VirtualizationRoot_GetRootRelativePath(root, vnodePath);
    
    int nextMessageId = OSIncrementAtomic(&s_nextMessageId);
    
    Message messageSpec = {};
    Message_Init(
        &messageSpec,
        &(message.request),
        nextMessageId,
        messageType,
        vnodeFsidInode,
        pid,
        procname,
        relativePath);

    bool isShuttingDown = false;
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        // Only read s_isShuttingDown once so we either insert & send message, or neither.
        isShuttingDown = s_isShuttingDown;
        if (!isShuttingDown)
        {
            LIST_INSERT_HEAD(&s_outstandingMessages, &message, _list_privates);
        }
    }
    Mutex_Release(s_outstandingMessagesMutex);
    
    // TODO(Mac): Should we pass in the root directly, rather than root->index?
    //            The index seems more like a private implementation detail.
    if (!isShuttingDown && 0 != ActiveProvider_SendMessage(root, messageSpec))
    {
        // TODO: appropriately handle unresponsive providers
        
        *kauthResult = KAUTH_RESULT_DEFER;
        goto CleanupAndReturn;
    }
    
    while (!message.receivedResponse &&
           !s_isShuttingDown)
    {
        Sleep(5, &message);
    }
    
    if (s_isShuttingDown)
    {
        *kauthResult = KAUTH_RESULT_DENY;
        goto CleanupAndReturn;
    }

    if (MessageType_Response_Success == message.response)
    {
        *kauthResult = KAUTH_RESULT_DEFER;
        result = true;
        goto CleanupAndReturn;
    }
    else
    {
        // Default error code is EACCES. See errno.h for more codes.
        *kauthError = EAGAIN;
        *kauthResult = KAUTH_RESULT_DENY;
        goto CleanupAndReturn;
    }
    
CleanupAndReturn:
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        LIST_REMOVE(&message, _list_privates);
    }
    Mutex_Release(s_outstandingMessagesMutex);
    
    return result;
}

static void AbortAllOutstandingEvents()
{
    // Wake up all sleeping threads so they can see that that we're shutting down and return an error
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        s_isShuttingDown = true;
        
        OutstandingMessage* outstandingMessage;
        LIST_FOREACH(outstandingMessage, &s_outstandingMessages, _list_privates)
        {
            wakeup(outstandingMessage);
        }
    }
    Mutex_Release(s_outstandingMessagesMutex);
    
    // ... and wait until all kauth events have noticed and returned.
    // Always sleeping at least once reduces the likelihood of a race condition
    // between kauth_unlisten_scope and the s_numActiveKauthEvents increment at
    // the start of the callback.
    // This race condition and the inability to work around it is a longstanding
    // bug in the xnu kernel - see comment block in RemoveListener() function of
    // the KauthORama sample code:
    // https://developer.apple.com/library/archive/samplecode/KauthORama/Listings/KauthORama_c.html#//apple_ref/doc/uid/DTS10003633-KauthORama_c-DontLinkElementID_3
    do
    {
        Sleep(1, NULL);
    } while (atomic_load(&s_numActiveKauthEvents) > 0);
}

static void Sleep(int seconds, void* channel)
{
    struct timespec timeout;
    timeout.tv_sec  = seconds;
    timeout.tv_nsec = 0;
    
    msleep(channel, nullptr, PUSER, "io.gvfs.PrjFSKext.Sleep", &timeout);
}

static int GetPid(vfs_context_t context)
{
    proc_t callingProcess = vfs_context_proc(context);
    return proc_pid(callingProcess);
}

static errno_t GetVNodeAttributes(vnode_t vn, vfs_context_t context, struct vnode_attr* attrs)
{
    VATTR_INIT(attrs);
    VATTR_WANTED(attrs, va_flags);
    
    return vnode_getattr(vn, attrs, context);
}

static uint32_t ReadVNodeFileFlags(vnode_t vn, vfs_context_t context)
{
    struct vnode_attr attributes = {};
    errno_t err = GetVNodeAttributes(vn, context, &attributes);
    // TODO: May fail on some file system types? Perhaps we should early-out depending on mount point anyway.
    assert(0 == err);
    assert(VATTR_IS_SUPPORTED(&attributes, va_flags));
    return attributes.va_flags;
}

static inline bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit)
{
    // Note: if multiple bits are set in 'bit', this will return true if ANY are set in fileFlags
    return 0 != (fileFlags & bit);
}

static inline bool FileIsFlaggedAsInRoot(vnode_t vnode, vfs_context_t context)
{
    uint32_t vnodeFileFlags = ReadVNodeFileFlags(vnode, context);
    return FileFlagsBitIsSet(vnodeFileFlags, FileFlags_IsInVirtualizationRoot);
}
static inline bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask)
{
    return action & mask;
}

static bool IsFileSystemCrawler(char* procname)
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

static bool ShouldIgnoreVnodeType(vtype vnodeType, vnode_t vnode)
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
        {
            char vnodePath[PrjFSMaxPath];
            int vnodePathLength = PrjFSMaxPath;
            vn_getpath(vnode, vnodePath, &vnodePathLength);
            KextLog_Info("vnode with type %s encountered, path %s", vnodeType == VSTR ? "VSTR" : "VCPLX", vnodePath);
            
            return false;
        }
    default:
        KextLog_Info("vnode with unknown type %d encountered", vnodeType);
        return false;
    }
}
