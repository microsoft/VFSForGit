#include <kern/debug.h>
#include <kern/assert.h>
#include <sys/proc.h>

#include "public/PrjFSCommon.h"
#include "public/PrjFSXattrs.h"
#include "Message_Kernel.hpp"
#include "VirtualizationRoots.hpp"
#include "VirtualizationRootsPrivate.hpp"
#include "Memory.hpp"
#include "Locks.hpp"
#include "KextLog.hpp"
#include "PrjFSProviderUserClient.hpp"
#include "ProviderMessaging.hpp"
#include "kernel-header-wrappers/mount.h"
#include "kernel-header-wrappers/stdatomic.h"
#include "VnodeUtilities.hpp"
#include "PerformanceTracing.hpp"
#include "ArrayUtilities.hpp"

#ifdef KEXT_UNIT_TESTING
#include "VirtualizationRootsTestable.hpp"
#endif

static RWLock s_virtualizationRootsLock = {};

// Current length of the s_virtualizationRoots array
KEXT_STATIC uint16_t s_maxVirtualizationRoots = 0;
static const uint16_t MaxVirtualizationRootsLimit = INT16_MAX + 1u;
KEXT_STATIC VirtualizationRoot* s_virtualizationRoots = nullptr;

static constexpr uint32_t MaxOfflineIOPIDs = 128;
// Also protected by the lock
static uint32_t s_offlineIOPIDCount = 0;
static pid_t    s_offlineIOPIDs[MaxOfflineIOPIDs] = {};

// Looks up the vnode/vid and fsid/inode pairs among the known roots
static VirtualizationRootHandle FindRootAtVnode_Locked(vnode_t vnode, uint32_t vid, FsidInode fileId);

static void RefreshRootVnodeIfNecessary_Locked(VirtualizationRootHandle rootHandle, vnode_t vnode, uint32_t vid, FsidInode fileId);
static bool FsidsAreEqual(fsid_t a, fsid_t b);
KEXT_STATIC bool PathInsideDirectory(const char* directoryPath, const char* path);
static void GrowVirtualizationRootArrayWithMemory_Locked(VirtualizationRoot* newMemory, uint16_t newLength);

// Looks up the vnode and fsid/inode pair among the known roots, and if not found,
// detects if there is a hitherto-unknown root at vnode by checking attributes.
static VirtualizationRootHandle FindOrDetectRootAtVnode(vnode_t vnode, const FsidInode& vnodeFsidInode);

static VirtualizationRootHandle FindUnusedIndex_Locked();

KEXT_STATIC VirtualizationRootHandle FindOrInsertVirtualizationRoot_LockedMayUnlock(vnode_t vnode, uint32_t vid, FsidInode persistentIds, const char* path);

ActiveProviderProperties VirtualizationRoot_GetActiveProvider(VirtualizationRootHandle rootHandle)
{
    ActiveProviderProperties result = { false, 0 };
    if (rootHandle < 0)
    {
        return result;
    }
    
    RWLock_AcquireShared(s_virtualizationRootsLock);
    {
        result.isOnline =
            rootHandle < s_maxVirtualizationRoots
            && s_virtualizationRoots[rootHandle].inUse
            && nullptr != s_virtualizationRoots[rootHandle].providerUserClient;
        
        if (result.isOnline)
        {
            result.pid = s_virtualizationRoots[rootHandle].providerPid;
        }
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);
    
    return result;
}

bool VirtualizationRoot_IsValidRootHandle(VirtualizationRootHandle rootIndex)
{
    return (rootIndex > RootHandle_None);
}

kern_return_t VirtualizationRoots_Init()
{
    if (RWLock_IsValid(s_virtualizationRootsLock))
    {
        return KERN_FAILURE;
    }
    
    s_virtualizationRootsLock = RWLock_Alloc();
    if (!RWLock_IsValid(s_virtualizationRootsLock))
    {
        return KERN_FAILURE;
    }
    
    // Start with a small size so the resizing logic is regularly tested
    s_maxVirtualizationRoots = 4;
    s_virtualizationRoots = Memory_AllocArray<VirtualizationRoot>(s_maxVirtualizationRoots);
    if (nullptr == s_virtualizationRoots)
    {
        return KERN_RESOURCE_SHORTAGE;
    }
    
    for (VirtualizationRootHandle i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        s_virtualizationRoots[i] = VirtualizationRoot{ };
    }
    
    atomic_thread_fence(memory_order_seq_cst);
    
    return KERN_SUCCESS;
}

kern_return_t VirtualizationRoots_Cleanup()
{
    if (s_virtualizationRoots != nullptr)
    {
        for (uint32_t i = 0; i < s_maxVirtualizationRoots; ++i)
        {
            // If there are still providers registered at this point, we will leak vnodes
            assert(s_virtualizationRoots[i].providerUserClient == nullptr);
        }
        
        Memory_FreeArray(s_virtualizationRoots, s_maxVirtualizationRoots);
        s_virtualizationRoots = nullptr;
        s_maxVirtualizationRoots = 0;
    }

    assert(s_offlineIOPIDCount == 0);

    if (RWLock_IsValid(s_virtualizationRootsLock))
    {
        RWLock_FreeMemory(&s_virtualizationRootsLock);
        return KERN_SUCCESS;
    }
    
    return KERN_FAILURE;
}

VirtualizationRootHandle VirtualizationRoot_FindForVnode(
    PerfTracer* _Nonnull perfTracer,
    PrjFSPerfCounter functionCounter,
    PrjFSPerfCounter innerLoopCounter,
    vnode_t _Nonnull initialVnode,
    vfs_context_t _Nonnull context)
{
    PerfSample findForVnodeSample(perfTracer, functionCounter);
    
    VirtualizationRootHandle rootHandle = RootHandle_None;
    errno_t error = 0;
    vnode_t vnode = initialVnode;

    if (vnode_isdir(vnode))
    {
        error = vnode_get(vnode);
        if (error != 0)
        {
            KextLog_ErrorVnodePathAndProperties(vnode, "VirtualizationRoot_FindForVnode: vnode_get() failed (error = %d) on vnode %p:%u which we'd expect to be live", error, KextLog_Unslide(vnode), vnode_vid(vnode));
            return RootHandle_None;
        }
    }
    else
    {
        vnode = vnode_getparent(vnode);
    }
    
    // Search up the tree until we hit a known virtualization root or THE root of the file system
    while (RootHandle_None == rootHandle && NULLVP != vnode && !vnode_isvroot(vnode))
    {
        PerfSample iterationSample(perfTracer, innerLoopCounter);

        FsidInode vnodeFsidInode = Vnode_GetFsidAndInode(vnode, context, false /* Here we care about identity, not path */);

        rootHandle = FindOrDetectRootAtVnode(vnode, vnodeFsidInode);
        
        // If FindOrDetectRootAtVnode returned a "special" handle other
        // than RootHandle_None, we want to stop the search and return that.
        if (rootHandle != RootHandle_None)
        {
            break;
        }
        
        vnode_t parent = vnode_getparent(vnode);
        if (NULLVP == parent)
        {
            KextLog_FileError(vnode, "VirtualizationRoot_FindForVnode: vnode_getparent returned nullptr on vnode that is not root of a mount point");
        }
        vnode_put(vnode);
        vnode = parent;
    }
    
    if (NULLVP != vnode)
    {
        vnode_put(vnode);
    }
    
    return rootHandle;
}

static VirtualizationRootHandle FindOrDetectRootAtVnode(vnode_t _Nonnull vnode, const FsidInode& vnodeFsidInode)
{
    uint32_t vid = vnode_vid(vnode);
    
    VirtualizationRootHandle rootIndex;
    bool rootVnodeStale = false;
    
    RWLock_AcquireShared(s_virtualizationRootsLock);
    {
        rootIndex = FindRootAtVnode_Locked(vnode, vid, vnodeFsidInode);
        rootVnodeStale =
            rootIndex >= 0
            && (s_virtualizationRoots[rootIndex].rootVNode != vnode
                || s_virtualizationRoots[rootIndex].rootVNodeVid != vid);
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);
    
    if (rootIndex == RootHandle_None)
    {
        PrjFSVirtualizationRootXAttrData rootXattr = {};
        SizeOrError xattrResult = Vnode_ReadXattr(vnode, PrjFSVirtualizationRootXAttrName, &rootXattr, sizeof(rootXattr));
        if (xattrResult.error == 0)
        {
            // TODO: check xattr contents
            
            const char* path = nullptr;
#if DEBUG // Offline roots shouldn't need their path filled, and vn_getpath() may fail anyway. Poison the value so any dependency will trip over it.
            char pathBuffer[PrjFSMaxPath + 6] = "DEBUG:";
            int pathLength = static_cast<int>(sizeof(pathBuffer) - strlen(pathBuffer));
            assertf(pathLength >= PATH_MAX, "Poisoning the string shouldn't make the buffer too short (vn_getpath expects PATH_MAX = %u, got %u)", PATH_MAX, pathLength);
            errno_t error = vn_getpath(vnode, pathBuffer + strlen(pathBuffer), &pathLength);
            if (error != 0)
            {
                KextLog_ErrorVnodeProperties(vnode, "FindOrDetectRootAtVnode: vn_getpath failed (error = %d)", error);
            }
            else
            {
                path = pathBuffer;
            }
#endif
 
            RWLock_AcquireExclusive(s_virtualizationRootsLock);
            {
                rootIndex = FindOrInsertVirtualizationRoot_LockedMayUnlock(vnode, vid, vnodeFsidInode, path);
                
                // TODO: error handling
                assert(rootIndex >= 0);
            }
            RWLock_ReleaseExclusive(s_virtualizationRootsLock);
        }
        else if (xattrResult.error != ENOATTR)
        {
            KextLog_FileError(vnode, "FindOrDetectRootAtVnode: Vnode_ReadXattr/mac_vnop_getxattr failed with errno %d", xattrResult.error);
        }
    }
    else if (rootVnodeStale)
    {
        RWLock_AcquireExclusive(s_virtualizationRootsLock);
        {
            RefreshRootVnodeIfNecessary_Locked(rootIndex, vnode, vid, vnodeFsidInode);
        }
        RWLock_ReleaseExclusive(s_virtualizationRootsLock);
    }
    
    return rootIndex;
}

static VirtualizationRootHandle FindUnusedIndex_Locked()
{
    for (uint32_t i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        if (!s_virtualizationRoots[i].inUse)
        {
            return i;
        }
    }
    
    return RootHandle_None;
}

static void GrowVirtualizationRootArrayWithMemory_Locked(VirtualizationRoot* newMemory, uint16_t newLength)
{
    uint32_t oldSizeBytes = sizeof(s_virtualizationRoots[0]) * s_maxVirtualizationRoots;
    memcpy(newMemory, s_virtualizationRoots, oldSizeBytes);
    Memory_FreeArray(s_virtualizationRoots, s_maxVirtualizationRoots);
    s_virtualizationRoots = newMemory;

    Array_DefaultInit(&s_virtualizationRoots[s_maxVirtualizationRoots], newLength - s_maxVirtualizationRoots);

    s_maxVirtualizationRoots = newLength;
}

static bool FsidsAreEqual(fsid_t a, fsid_t b)
{
    return a.val[0] == b.val[0] && a.val[1] == b.val[1];
}

static VirtualizationRootHandle FindRootAtVnode_Locked(vnode_t vnode, uint32_t vid, FsidInode fileId)
{
    for (uint32_t i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        VirtualizationRoot& rootEntry = s_virtualizationRoots[i];
        if (!rootEntry.inUse)
        {
            continue;
        }
        
        if (rootEntry.rootVNode == vnode && rootEntry.rootVNodeVid == vid)
        {
            assertf(
                FsidsAreEqual(fileId.fsid, rootEntry.rootFsid) && fileId.inode == rootEntry.rootInode,
                "FindRootAtVnode_Locked: matching root vnode/vid but not fsid/inode? vnode %p:%u. rootEntry fsid 0x%x:%x, inode 0x%llx, searching for fsid 0x%x:%x, inode 0x%llx",
                KextLog_Unslide(vnode), vid, rootEntry.rootFsid.val[0], rootEntry.rootFsid.val[1], rootEntry.rootInode, fileId.fsid.val[0], fileId.fsid.val[1], fileId.inode);
            return i;
        }
        else if (rootEntry.rootVNode == vnode)
        {
            assert(rootEntry.providerUserClient == nullptr);
        }
        
        
        if (FsidsAreEqual(rootEntry.rootFsid, fileId.fsid) && rootEntry.rootInode == fileId.inode)
        {
            assertf(rootEntry.providerUserClient == nullptr, "Finding root vnode based on FSID/inode equality but not vnode identity (recycled vnode) should only happen if no provider is active. Root index %d, provider PID %d, IOUC %p path '%s'",
                i, rootEntry.providerPid, KextLog_Unslide(rootEntry.providerUserClient), rootEntry.path);
            // root vnode must be stale
            KextLog_File(vnode, "FindRootAtVnode_Locked: virtualization root %d (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u has gone stale, new vnode %p:%u",
                i, rootEntry.path, rootEntry.rootFsid.val[0], rootEntry.rootFsid.val[1], rootEntry.rootInode, KextLog_Unslide(rootEntry.rootVNode), rootEntry.rootVNodeVid, KextLog_Unslide(vnode), vid);
            return i;
        }
    }
    
    return RootHandle_None;
}

static void RefreshRootVnodeIfNecessary_Locked(VirtualizationRootHandle rootHandle, vnode_t vnode, uint32_t vid, FsidInode fileId)
{
    VirtualizationRoot& rootEntry = s_virtualizationRoots[rootHandle];
    if (rootEntry.rootVNode == vnode && rootEntry.rootVNodeVid == vid)
    {
        return;
    }

    assertf(
        FsidsAreEqual(rootEntry.rootFsid, fileId.fsid) && rootEntry.rootInode == fileId.inode,
        "RefreshRootVnodeIfNecessary_Locked: expecting matching FSID/inode for new vnode on root %d (%s). "
        "Root's: FSID 0x%x:%x, inode 0x%llx; new vnode's FSID: 0x%x:%x, inode: 0x%llx; "
        "old vnode %p:%u, new %p:%u",
        rootHandle, rootEntry.path,
        rootEntry.rootFsid.val[0], rootEntry.rootFsid.val[1], rootEntry.rootInode, fileId.fsid.val[0], fileId.fsid.val[1], fileId.inode,
        KextLog_Unslide(rootEntry.rootVNode), rootEntry.rootVNodeVid, KextLog_Unslide(vnode), vid);

    assertf(
        rootEntry.providerUserClient == nullptr,
        "RefreshRootVnodeIfNecessary_Locked: only root vnodes with no active provider should be recycled! Virtualization root %d (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u has gone stale, refreshing with new vnode %p:%u",
        rootHandle, rootEntry.path, rootEntry.rootFsid.val[0], rootEntry.rootFsid.val[1], rootEntry.rootInode, KextLog_Unslide(rootEntry.rootVNode), rootEntry.rootVNodeVid, KextLog_Unslide(vnode), vid);
    KextLog_File(vnode, "RefreshRootVnodeIfNecessary_Locked: virtualization root %d (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u has gone stale, refreshing with new vnode %p:%u",
        rootHandle, rootEntry.path, rootEntry.rootFsid.val[0], rootEntry.rootFsid.val[1], rootEntry.rootInode, KextLog_Unslide(rootEntry.rootVNode), rootEntry.rootVNodeVid, KextLog_Unslide(vnode), vid);
    rootEntry.rootVNode = vnode;
    rootEntry.rootVNodeVid = vid;
}

KEXT_STATIC VirtualizationRootHandle FindOrInsertVirtualizationRoot_LockedMayUnlock(vnode_t virtualizationRootVNode, uint32_t rootVid, FsidInode persistentIds, const char* path)
{
    VirtualizationRootHandle rootIndex;
    bool allocFailed = false;
    do
    {
        rootIndex = FindRootAtVnode_Locked(virtualizationRootVNode, rootVid, persistentIds);
        if (rootIndex >= 0)
        {
            return rootIndex;
        }
        
        rootIndex = FindUnusedIndex_Locked();
        if (rootIndex < 0)
        {
            // No space, resize array
            uint16_t newLength = MIN(s_maxVirtualizationRoots * 2u, MaxVirtualizationRootsLimit);
            if (newLength <= s_maxVirtualizationRoots || allocFailed)
            {
                KextLog_Error(
                    "FindOrInsertVirtualizationRoot_LockedMayUnlock: growing virtualization root array for root at '%s' failed: %s, array has reached %u items, resize target %u items\n",
                    path,
                    allocFailed ? "memory allocation failed" : "out of valid indices",
                    s_maxVirtualizationRoots,
                    newLength);
                return RootHandle_None;
            }
        
            // Must drop lock to safely allocate memory with blocking alloc
            RWLock_ReleaseExclusive(s_virtualizationRootsLock);
            VirtualizationRoot* grownArray = Memory_AllocArray<VirtualizationRoot>(newLength);
            RWLock_AcquireExclusive(s_virtualizationRootsLock);
            
            if (grownArray == nullptr)
            {
                // Allocation failed; recheck for space since relocking, and if that fails, give up.
                allocFailed = true;
                continue;
            }
            
            uint16_t newLengthAgain = MIN(s_maxVirtualizationRoots * 2u, MaxVirtualizationRootsLimit);
            if (newLengthAgain != newLength || s_maxVirtualizationRoots == MaxVirtualizationRootsLimit)
            {
                KextLog_Info("FindOrInsertVirtualizationRoot_LockedMayUnlock: Beaten to the resize (newLength = %u, newLengthAgain = %u) by another thread, starting over.", newLength, newLengthAgain);
                // another thread already resized, start over.
                Memory_FreeArray(grownArray, newLength);
                continue;
            }
            
            GrowVirtualizationRootArrayWithMemory_Locked(grownArray, newLength);
        }
    } while (rootIndex < 0);

    assert(rootIndex < s_maxVirtualizationRoots);
    assert(!s_virtualizationRoots[rootIndex].inUse);

    VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
    
    root->inUse = true;

    root->rootVNode = virtualizationRootVNode;
    root->rootVNodeVid = rootVid;
    root->rootFsid = persistentIds.fsid;
    root->rootInode = persistentIds.inode;
    if (path != nullptr)
    {
        strlcpy(root->path, path, sizeof(root->path));
    }

    KextLog_File(virtualizationRootVNode, "FindOrInsertVirtualizationRoot_LockedMayUnlock: virtualization root inserted at index %d: (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u.",
            rootIndex, path, persistentIds.fsid.val[0], persistentIds.fsid.val[1], persistentIds.inode, KextLog_Unslide(virtualizationRootVNode), rootVid);

    return rootIndex;
}

// Return values:
// 0:        Virtualization root found and successfully registered
// ENOMEM:   Too many virtualization roots.
// ENOTDIR:  Selected virtualization root path does not resolve to a directory.
// EBUSY:    Already a provider for this virtualization root.
// ENOENT:   Error returned by vnode_lookup.
// Any error returned by the call to vn_getpath()
VirtualizationRootResult VirtualizationRoot_RegisterProviderForPath(PrjFSProviderUserClient* userClient, pid_t clientPID, const char* virtualizationRootPath)
{
    assert(nullptr != virtualizationRootPath);
    assert(nullptr != userClient);
    
    vnode_t virtualizationRootVNode = NULLVP;
    vfs_context_t _Nonnull vfsContext = vfs_context_create(nullptr);
    
    VirtualizationRootHandle rootIndex = RootHandle_None;
    errno_t err = vnode_lookup(virtualizationRootPath, 0 /* flags */, &virtualizationRootVNode, vfsContext);
    if (0 == err)
    {
        if (!VirtualizationRoot_VnodeIsOnAllowedFilesystem(virtualizationRootVNode))
        {
            err = ENODEV;
        }
        else if (!vnode_isdir(virtualizationRootVNode))
        {
            err = ENOTDIR;
        }
        else
        {
            char virtualizationRootCanonicalPath[PrjFSMaxPath] = "";
            int virtualizationRootCanonicalPathLength = sizeof(virtualizationRootCanonicalPath);
            err = vn_getpath(virtualizationRootVNode, virtualizationRootCanonicalPath, &virtualizationRootCanonicalPathLength);
            
            if (0 != err)
            {
                KextLog_ErrorVnodeProperties(
                    virtualizationRootVNode, "VirtualizationRoot_RegisterProviderForPath: vn_getpath failed (error = %d) for vnode looked up from path '%s'", err, virtualizationRootPath);
            }
            else
            {
                FsidInode vnodeIds = Vnode_GetFsidAndInode(virtualizationRootVNode, vfsContext, false /* If a root has multiple hardlinks to it, only track one instance of it. */);
                uint32_t rootVid = vnode_vid(virtualizationRootVNode);
                
                RWLock_AcquireExclusive(s_virtualizationRootsLock);
                {
                    rootIndex = FindOrInsertVirtualizationRoot_LockedMayUnlock(virtualizationRootVNode, rootVid, vnodeIds, virtualizationRootCanonicalPath);
                    
                    if (rootIndex >= 0)
                    {
                        RefreshRootVnodeIfNecessary_Locked(rootIndex, virtualizationRootVNode, rootVid, vnodeIds);
                        
                        VirtualizationRoot& root = s_virtualizationRoots[rootIndex];
                        assert(root.rootVNode == virtualizationRootVNode);

                        if (nullptr != root.providerUserClient)
                        {
                            // Only one provider per root
                            err = EBUSY;
                            rootIndex = RootHandle_None;
                        }
                        else
                        {
                            root.providerUserClient = userClient;
                            root.providerPid = clientPID;
                            strlcpy(root.path, virtualizationRootCanonicalPath, sizeof(root.path));
                            KextLog_File(virtualizationRootVNode, "VirtualizationRoot_RegisterProviderForPath: registered provider (PID %d, IOUC %p) for virtualization root %d: (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u.",
                                clientPID, KextLog_Unslide(userClient), rootIndex, root.path, root.rootFsid.val[0], root.rootFsid.val[1], root.rootInode, KextLog_Unslide(virtualizationRootVNode), rootVid);

                            // Acquire strong reference while provider is connected
                            err = vnode_get(virtualizationRootVNode);
                            assert(err == 0); // we already hold 1 strong reference from vnode_lookup so this should always succeed
                        }
                    }
                    else
                    {
                        KextLog_Error("VirtualizationRoot_RegisterProviderForPath: failed to insert new root '%s' for provider with PID %u", virtualizationRootPath, clientPID);
                        err = ENOMEM;
                    }
                }
                RWLock_ReleaseExclusive(s_virtualizationRootsLock);
                
                if (0 == err)
                {
                    ProviderUserClient_UpdatePathProperty(userClient, virtualizationRootCanonicalPath);
                }
            }
        }
    }
    
    if (VirtualizationRoot_IsValidRootHandle(rootIndex))
    {
        vfs_setauthcache_ttl(vnode_mount(virtualizationRootVNode), 0);
    }

    if (NULLVP != virtualizationRootVNode)
    {
        vnode_put(virtualizationRootVNode);
    }

    vfs_context_rele(vfsContext);
    
    return VirtualizationRootResult { err, rootIndex };
}

void ActiveProvider_Disconnect(VirtualizationRootHandle rootIndex, PrjFSProviderUserClient* _Nonnull userClient)
{
    assert(rootIndex >= 0);
    RWLock_AcquireExclusive(s_virtualizationRootsLock);
    {
        assert(rootIndex <= s_maxVirtualizationRoots);

        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        assert(nullptr != root->providerUserClient);
        assertf(userClient == root->providerUserClient, "ActiveProvider_Disconnect: disconnecting provider IOUC %p for root index %d (%s), but expecting IOUC %p",
            KextLog_Unslide(userClient), rootIndex, root->path, KextLog_Unslide(root->providerUserClient));
        
        assert(NULLVP != root->rootVNode);
        
        KextLog_File(root->rootVNode, "ActiveProvider_Disconnect: disconnecting provider (PID %d, IOUC %p) for virtualization root %d: (path: \"%s\", fsid: 0x%x:%x, inode: 0x%llx) directory vnode %p:%u.",
            root->providerPid, KextLog_Unslide(root->providerUserClient), rootIndex, root->path, root->rootFsid.val[0], root->rootFsid.val[1], root->rootInode, KextLog_Unslide(root->rootVNode), root->rootVNodeVid);

        
        vnode_put(root->rootVNode);
        root->providerPid = 0;
        
        root->providerUserClient = nullptr;

        RWLock_DropExclusiveToShared(s_virtualizationRootsLock);

        ProviderMessaging_AbortOutstandingEventsForProvider(rootIndex);
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);
}

errno_t ActiveProvider_SendMessage(VirtualizationRootHandle rootIndex, const Message& message)
{
    assert(rootIndex >= 0);

    PrjFSProviderUserClient* userClient = nullptr;
    
    RWLock_AcquireShared(s_virtualizationRootsLock);
    {
        assert(rootIndex < s_maxVirtualizationRoots);
        
        userClient = s_virtualizationRoots[rootIndex].providerUserClient;
        if (nullptr != userClient)
        {
            ProviderUserClient_Retain(userClient);
        }
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);
    
    if (nullptr != userClient)
    {
        uint32_t messageSize = Message_EncodedSize(message.messageHeader);
        uint8_t messageMemory[messageSize];
        uint32_t bytesUsed OS_UNUSED = Message_Encode(messageMemory, messageSize, message);
        assertf(bytesUsed == messageSize, "bytes used by Message_Encode (%u) should match Message_EncodedSize's prediction (%u)", bytesUsed, messageSize);
        
        ProviderUserClient_SendMessage(userClient, messageMemory, messageSize);
        ProviderUserClient_Release(userClient);
        return 0;
    }
    else
    {
        return EIO;
    }
}

bool VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode_t vnode)
{
    vfsstatfs* vfsStat = vfs_statfs(vnode_mount(vnode));
    return
        0 == strncmp("hfs", vfsStat->f_fstypename, sizeof(vfsStat->f_fstypename))
        || 0 == strncmp("apfs", vfsStat->f_fstypename, sizeof(vfsStat->f_fstypename));
}

KEXT_STATIC bool PathInsideDirectory(const char* directoryPath, const char* path)
{
    if (!strprefix(path, directoryPath))
    {
        return false;
    }
    
    // string prefix alone is not sufficient, must not return true for the following:
    //   /path/to/some/dir
    //   /path/to/some/directory/containing/file
    // so check for the '/' positioning:
    
    size_t directoryPathLength = strlen(directoryPath);
    if (directoryPathLength >= 1 && directoryPath[directoryPathLength - 1] == '/')
    {
        // directoryPath ends with a "/", so no ambiguity
        return true;
    }
    else if (path[directoryPathLength] == '\0' // path is identical to directoryPath
             || path[directoryPathLength] == '/') // path really is strictly below directoryPath
    {
        return true;
    }

    return false;
}

VirtualizationRootHandle ActiveProvider_FindForPath(const char* _Nonnull path)
{
    VirtualizationRootHandle matchingHandle = RootHandle_None;
    
    RWLock_AcquireShared(s_virtualizationRootsLock);
    {
        for (uint32_t i = 0; i < s_maxVirtualizationRoots; ++i)
        {
            VirtualizationRoot& root = s_virtualizationRoots[i];
            if (root.inUse && root.providerUserClient != nullptr && PathInsideDirectory(root.path, path))
            {
                matchingHandle = i;
                break;
            }
        }
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);
    
    return matchingHandle;
}

/// Tests whether pid or its parent, grandparent, etc. process is registered for offline I/O
bool VirtualizationRoots_ProcessMayAccessOfflineRoots(pid_t pid)
{
    bool result = false;
    
    RWLock_AcquireShared(s_virtualizationRootsLock);
    {
        while (true)
        {
            for (uint32_t i = 0; i < s_offlineIOPIDCount; ++i)
            {
                if (s_offlineIOPIDs[i] == pid)
                {
                    result = true;
                    goto done;
                }
            }

            // Walk up the process hierarchy
            proc_t process = proc_find(pid);
            if (process == nullptr)
            {
                // Process exited since last proc_ppid call.
                break;
            }
            
            pid = proc_ppid(process);
            proc_rele(process);
            
            // Stop when we hit the launchd root process
            if (pid <= 1)
            {
                break;
            }
        }
        done: {}
    }
    RWLock_ReleaseShared(s_virtualizationRootsLock);

    return result;
}

bool VirtualizationRoots_AddOfflineIOProcess(pid_t pid)
{
    bool success = false;
    
    RWLock_AcquireExclusive(s_virtualizationRootsLock);
    {
        if (s_offlineIOPIDCount < MaxOfflineIOPIDs)
        {
            s_offlineIOPIDs[s_offlineIOPIDCount] = pid;
            ++s_offlineIOPIDCount;
            success = true;
        }
    }
    RWLock_ReleaseExclusive(s_virtualizationRootsLock);
    
    return success;
}

void VirtualizationRoots_RemoveOfflineIOProcess(pid_t pid)
{
    bool removed = false;
    
    RWLock_AcquireExclusive(s_virtualizationRootsLock);
    {
        assert(s_offlineIOPIDCount > 0);

        for (uint32_t i = 0; i < s_offlineIOPIDCount; ++i)
        {
            if (s_offlineIOPIDs[i] == pid)
            {
                --s_offlineIOPIDCount;
                if (i != s_offlineIOPIDCount)
                {
                    // move last element to fill vacated slot
                    s_offlineIOPIDs[i] = s_offlineIOPIDs[s_offlineIOPIDCount];
                }
                
                removed = true;
                break;
            }
        }
    }
    RWLock_ReleaseExclusive(s_virtualizationRootsLock);
    
    assert(removed);
}
