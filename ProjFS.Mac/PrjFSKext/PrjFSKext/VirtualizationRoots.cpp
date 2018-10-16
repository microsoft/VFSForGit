#include <kern/debug.h>
#include <kern/assert.h>
#include <stdatomic.h>

#include "PrjFSCommon.h"
#include "PrjFSXattrs.h"
#include "VirtualizationRoots.hpp"
#include "Memory.hpp"
#include "Locks.hpp"
#include "KextLog.hpp"
#include "PrjFSProviderUserClient.hpp"
#include "kernel-header-wrappers/mount.h"
#include "VnodeUtilities.hpp"
#include "PerformanceTracing.hpp"


struct VirtualizationRoot
{
    bool                        inUse;
    // If this is a nullptr, there is no active provider for this virtualization root (offline root)
    PrjFSProviderUserClient*    providerUserClient;
    int                         providerPid;
    // For an active root, this is retained (vnode_get), for an offline one, it is not, so it may be stale (check the vid)
    vnode_t                     rootVNode;
    uint32_t                    rootVNodeVid;
    
    // Mount point ID + persistent, on-disk ID for the root directory, so we can
    // identify it if the vnode of an offline root gets recycled.
    fsid_t                      rootFsid;
    uint64_t                    rootInode;
    
    // TODO(Mac): this should eventually be entirely diagnostic and not used for decisions
    char                        path[PrjFSMaxPath];
};

static RWLock s_rwLock = {};

// Current length of the s_virtualizationRoots array
static uint16_t s_maxVirtualizationRoots = 0;
static VirtualizationRoot* s_virtualizationRoots = nullptr;

// Looks up the vnode/vid and fsid/inode pairs among the known roots
static VirtualizationRootHandle FindRootAtVnode_Locked(vnode_t vnode, uint32_t vid, FsidInode fileId);

// Looks up the vnode and fsid/inode pair among the known roots, and if not found,
// detects if there is a hitherto-unknown root at vnode by checking attributes.
static VirtualizationRootHandle FindOrDetectRootAtVnode(vnode_t vnode, vfs_context_t context, const FsidInode& vnodeFsidInode);

static VirtualizationRootHandle FindUnusedIndex_Locked();
static VirtualizationRootHandle InsertVirtualizationRoot_Locked(PrjFSProviderUserClient* userClient, pid_t clientPID, vnode_t vnode, uint32_t vid, FsidInode persistentIds, const char* path);

bool VirtualizationRoot_IsOnline(VirtualizationRootHandle rootHandle)
{
    if (rootHandle < 0)
    {
        return false;
    }
    
    bool result;
    RWLock_AcquireShared(s_rwLock);
    {
        result =
            rootHandle < s_maxVirtualizationRoots
            && s_virtualizationRoots[rootHandle].inUse
            && nullptr != s_virtualizationRoots[rootHandle].providerUserClient;
    }
    RWLock_ReleaseShared(s_rwLock);
    
    return result;
}

bool VirtualizationRoot_PIDMatchesProvider(VirtualizationRootHandle rootHandle, pid_t pid)
{
    bool result;
    RWLock_AcquireShared(s_rwLock);
    {
        result =
            rootHandle >= 0
            && rootHandle < s_maxVirtualizationRoots
            && s_virtualizationRoots[rootHandle].inUse
            && nullptr != s_virtualizationRoots[rootHandle].providerUserClient
            && pid == s_virtualizationRoots[rootHandle].providerPid;
    }
    RWLock_ReleaseShared(s_rwLock);
    
    return result;
}

bool VirtualizationRoot_IsValidRootHandle(VirtualizationRootHandle rootIndex)
{
    return (rootIndex > RootHandle_None);
}

kern_return_t VirtualizationRoots_Init()
{
    if (RWLock_IsValid(s_rwLock))
    {
        return KERN_FAILURE;
    }
    
    s_rwLock = RWLock_Alloc();
    if (!RWLock_IsValid(s_rwLock))
    {
        return KERN_FAILURE;
    }
    
    s_maxVirtualizationRoots = 128;
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
    if (RWLock_IsValid(s_rwLock))
    {
        RWLock_FreeMemory(&s_rwLock);
        return KERN_SUCCESS;
    }
    
    return KERN_FAILURE;
}

VirtualizationRootHandle VirtualizationRoot_FindForVnode(vnode_t vnode, const FsidInode& vnodeFsidInode)
{
    ProfileSample functionSample(Probe_VirtualizationRoot_Find);

    VirtualizationRootHandle rootHandle = RootHandle_None;
    
    vnode_get(vnode);
    // Search up the tree until we hit a known virtualization root or THE root of the file system
    while (RootHandle_None == rootHandle && NULLVP != vnode && !vnode_isvroot(vnode))
    {
        ProfileSample iterationSample(Probe_VirtualizationRoot_FindIteration);

        rootHandle = FindOrDetectRootAtVnode(vnode, nullptr /* vfs context */, vnodeFsidInode);
        // Note: if FindOrDetectRootAtVnode returns a "special" handle other
        // than RootHandle_None, we want to stop the search and return that.
        if (rootHandle != RootHandle_None)
        {
            break;
        }
        
        vnode_t parent = vnode_getparent(vnode);
        vnode_put(vnode);
        vnode = parent;
    }
    
    if (NULLVP != vnode)
    {
        vnode_put(vnode);
    }
    
    return rootHandle;
}

static VirtualizationRootHandle FindOrDetectRootAtVnode(vnode_t vnode, vfs_context_t context, const FsidInode& vnodeFsidInode)
{
    uint32_t vid = vnode_vid(vnode);
    
    VirtualizationRootHandle rootIndex;
    
    RWLock_AcquireShared(s_rwLock);
    {
        rootIndex = FindRootAtVnode_Locked(vnode, vid, vnodeFsidInode);
    }
    RWLock_ReleaseShared(s_rwLock);
    
    if (rootIndex == RootHandle_None)
    {
        PrjFSVirtualizationRootXAttrData rootXattr = {};
        SizeOrError xattrResult = Vnode_ReadXattr(vnode, PrjFSVirtualizationRootXAttrName, &rootXattr, sizeof(rootXattr), context);
        if (xattrResult.error == 0)
        {
            // TODO: check xattr contents
            
            char path[PrjFSMaxPath] = "";
            int pathLength = sizeof(path);
            vn_getpath(vnode, path, &pathLength);
            
            RWLock_AcquireExclusive(s_rwLock);
            {
                // Vnode may already have been inserted as a root in the interim
                rootIndex = FindRootAtVnode_Locked(vnode, vid, vnodeFsidInode);
                
                if (RootHandle_None == rootIndex)
                {
                    // Insert new offline root
                    rootIndex = InsertVirtualizationRoot_Locked(nullptr, 0, vnode, vid, vnodeFsidInode, path);
                    
                    // TODO: error handling
                    assert(rootIndex >= 0);
                }

            }
            RWLock_ReleaseExclusive(s_rwLock);
        }
    }
    
    return rootIndex;
}

static VirtualizationRootHandle FindUnusedIndex_Locked()
{
    for (VirtualizationRootHandle i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        if (!s_virtualizationRoots[i].inUse)
        {
            return i;
        }
    }
    
    return RootHandle_None;
}

static VirtualizationRootHandle FindUnusedIndexOrGrow_Locked()
{
    VirtualizationRootHandle rootIndex = FindUnusedIndex_Locked();
    
    if (RootHandle_None == rootIndex)
    {
        // No space, resize array
        uint16_t newLength = MIN(s_maxVirtualizationRoots * 2u, INT16_MAX + 1u);
        if (newLength <= s_maxVirtualizationRoots)
        {
            return RootHandle_None;
        }
        
        VirtualizationRoot* grownArray = Memory_AllocArray<VirtualizationRoot>(newLength);
        if (nullptr == grownArray)
        {
            return RootHandle_None;
        }
        
        uint32_t oldSizeBytes = sizeof(s_virtualizationRoots[0]) * s_maxVirtualizationRoots;
        memcpy(grownArray, s_virtualizationRoots, oldSizeBytes);
        Memory_Free(s_virtualizationRoots, oldSizeBytes);
        s_virtualizationRoots = grownArray;

        for (uint16_t i = s_maxVirtualizationRoots; i < newLength; ++i)
        {
            s_virtualizationRoots[i] = VirtualizationRoot{ };
        }
        
        rootIndex = s_maxVirtualizationRoots;
        s_maxVirtualizationRoots = newLength;
    }
    
    return rootIndex;
}

static bool FsidsAreEqual(fsid_t a, fsid_t b)
{
    return a.val[0] == b.val[0] && a.val[1] == b.val[1];
}

static VirtualizationRootHandle FindRootAtVnode_Locked(vnode_t vnode, uint32_t vid, FsidInode fileId)
{
    for (VirtualizationRootHandle i = 0; i < s_maxVirtualizationRoots; ++i)
    {
        VirtualizationRoot& rootEntry = s_virtualizationRoots[i];
        if (!rootEntry.inUse)
        {
            continue;
        }
        
        if (rootEntry.rootVNode == vnode && rootEntry.rootVNodeVid == vid)
        {
            assert(fileId.fsid.val[0] == rootEntry.rootFsid.val[0]);
            return i;
        }
        
        if (FsidsAreEqual(rootEntry.rootFsid, fileId.fsid) && rootEntry.rootInode == fileId.inode)
        {
            // root vnode must be stale, update it
            rootEntry.rootVNode = vnode;
            rootEntry.rootVNodeVid = vid;
            return i;
        }
    }
    return RootHandle_None;
}

// Returns negative value if it failed, or inserted index on success
static VirtualizationRootHandle InsertVirtualizationRoot_Locked(PrjFSProviderUserClient* userClient, pid_t clientPID, vnode_t vnode, uint32_t vid, FsidInode persistentIds, const char* path)
{
    VirtualizationRootHandle rootIndex = FindUnusedIndexOrGrow_Locked();
    
    if (RootHandle_None != rootIndex)
    {
        assert(rootIndex < s_maxVirtualizationRoots);
        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        
        root->providerUserClient = userClient;
        root->providerPid = clientPID;
        root->inUse = true;

        root->rootVNode = vnode;
        root->rootVNodeVid = vid;
        root->rootFsid = persistentIds.fsid;
        root->rootInode = persistentIds.inode;
        strlcpy(root->path, path, sizeof(root->path));
    }
    
    return rootIndex;
}

// Return values:
// 0:        Virtualization root found and successfully registered
// ENOMEM:   Too many virtualization roots.
// ENOTDIR:  Selected virtualization root path does not resolve to a directory.
// EBUSY:    Already a provider for this virtualization root.
// ENOENT,…: Error returned by vnode_lookup.
VirtualizationRootResult VirtualizationRoot_RegisterProviderForPath(PrjFSProviderUserClient* userClient, pid_t clientPID, const char* virtualizationRootPath)
{
    assert(nullptr != virtualizationRootPath);
    assert(nullptr != userClient);
    
    vnode_t virtualizationRootVNode = NULLVP;
    vfs_context_t vfsContext = vfs_context_create(nullptr);
    
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
            FsidInode vnodeIds = Vnode_GetFsidAndInode(virtualizationRootVNode, vfsContext);
            uint32_t rootVid = vnode_vid(virtualizationRootVNode);
            
            RWLock_AcquireExclusive(s_rwLock);
            {
                rootIndex = FindRootAtVnode_Locked(virtualizationRootVNode, rootVid, vnodeIds);
                if (rootIndex >= 0)
                {
                    // Reattaching to existing root
                    if (nullptr != s_virtualizationRoots[rootIndex].providerUserClient)
                    {
                        // Only one provider per root
                        err = EBUSY;
                        rootIndex = RootHandle_None;
                    }
                    else
                    {
                        VirtualizationRoot& root = s_virtualizationRoots[rootIndex];
                        root.providerUserClient = userClient;
                        root.providerPid = clientPID;
                        virtualizationRootVNode = NULLVP; // transfer ownership
                    }
                }
                else
                {
                    rootIndex = InsertVirtualizationRoot_Locked(userClient, clientPID, virtualizationRootVNode, rootVid, vnodeIds, virtualizationRootPath);
                    if (rootIndex >= 0)
                    {
                        assert(rootIndex < s_maxVirtualizationRoots);
                        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
                    
                        strlcpy(root->path, virtualizationRootPath, sizeof(root->path));
                        virtualizationRootVNode = NULLVP; // prevent vnode_put later; active provider should hold vnode reference
                    
                        KextLog_Note("VirtualizationRoot_RegisterProviderForPath: new root not found in offline roots, inserted as new root with index %d. path '%s'", rootIndex, virtualizationRootPath);
                    }
                    else
                    {
                        // TODO: scan the array for roots on mounts which have disappeared, or grow the array
                        KextLog_Error("VirtualizationRoot_RegisterProviderForPath: failed to insert new root");
                    }
                }
            }
            RWLock_ReleaseExclusive(s_rwLock);
        }
    }
    
    if (NULLVP != virtualizationRootVNode)
    {
        vnode_put(virtualizationRootVNode);
    }
    
    if (rootIndex >= 0)
    {
        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        vfs_setauthcache_ttl(vnode_mount(root->rootVNode), 0);
    }
    
    vfs_context_rele(vfsContext);
    
    return VirtualizationRootResult { err, rootIndex };
}

void ActiveProvider_Disconnect(VirtualizationRootHandle rootIndex)
{
    assert(rootIndex >= 0);
    RWLock_AcquireExclusive(s_rwLock);
    {
        assert(rootIndex <= s_maxVirtualizationRoots);

        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        assert(nullptr != root->providerUserClient);
        
        assert(NULLVP != root->rootVNode);
        vnode_put(root->rootVNode);
        root->providerPid = 0;
        
        root->providerUserClient = nullptr;
    }
    RWLock_ReleaseExclusive(s_rwLock);
}

errno_t ActiveProvider_SendMessage(VirtualizationRootHandle rootIndex, const Message message)
{
    assert(rootIndex >= 0);

    PrjFSProviderUserClient* userClient = nullptr;
    
    RWLock_AcquireShared(s_rwLock);
    {
        assert(rootIndex < s_maxVirtualizationRoots);
        
        userClient = s_virtualizationRoots[rootIndex].providerUserClient;
        if (nullptr != userClient)
        {
            userClient->retain();
        }
    }
    RWLock_ReleaseShared(s_rwLock);
    
    if (nullptr != userClient)
    {
        uint32_t messageSize = sizeof(*message.messageHeader) + message.messageHeader->pathSizeBytes;
        uint8_t messageMemory[messageSize];
        memcpy(messageMemory, message.messageHeader, sizeof(*message.messageHeader));
        if (message.messageHeader->pathSizeBytes > 0)
        {
            memcpy(messageMemory + sizeof(*message.messageHeader), message.path, message.messageHeader->pathSizeBytes);
        }
        
        userClient->sendMessage(messageMemory, messageSize);
        userClient->release();
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

static const char* GetRelativePath(const char* path, const char* root)
{
    assert(strlen(path) >= strlen(root));
    
    const char* relativePath = path + strlen(root);
    if (relativePath[0] == '/')
    {
        relativePath++;
    }
    
    return relativePath;
}

const char* VirtualizationRoot_GetRootRelativePath(VirtualizationRootHandle rootIndex, const char* path)
{
    assert(rootIndex >= 0);

    const char* relativePath;
    
    RWLock_AcquireShared(s_rwLock);
    {
        assert(rootIndex < s_maxVirtualizationRoots);
        assert(s_virtualizationRoots[rootIndex].inUse);
        relativePath = GetRelativePath(path, s_virtualizationRoots[rootIndex].path);
    }
    RWLock_ReleaseShared(s_rwLock);
    
    return relativePath;
}
