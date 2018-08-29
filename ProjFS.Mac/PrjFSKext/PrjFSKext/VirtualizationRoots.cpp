#include <kern/debug.h>
#include <kern/assert.h>

#include "PrjFSCommon.h"
#include "PrjFSXattrs.h"
#include "VirtualizationRoots.hpp"
#include "Memory.hpp"
#include "Locks.hpp"
#include "KextLog.hpp"
#include "PrjFSProviderUserClient.hpp"
#include "kernel-header-wrappers/mount.h"
#include "VnodeUtilities.hpp"


static RWLock s_rwLock = {};

// Arbitrary choice, but prevents user space attacker from causing
// allocation of too much wired kernel memory.
static const size_t MaxVirtualizationRoots = 128;

static VirtualizationRoot s_virtualizationRoots[MaxVirtualizationRoots] = {};

static int16_t FindRootForVnode_Locked(vnode_t vnode, uint32_t vid, VnodeFsidInode fileId);
static int16_t FindUnusedIndex_Locked();
static int16_t InsertVirtualizationRoot_Locked(PrjFSProviderUserClient* userClient, pid_t clientPID, vnode_t vnode, uint32_t vid, VnodeFsidInode persistentIds, const char* path, vnode_t temporaryDirectory);

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
    
    for (uint32_t i = 0; i < MaxVirtualizationRoots; ++i)
    {
        s_virtualizationRoots[i].index = i;
    }
    
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

VirtualizationRoot* VirtualizationRoots_FindForVnode(vnode_t vnode)
{
    VirtualizationRoot* root = nullptr;
    
    vnode_get(vnode);
    // Search up the tree until we hit a known virtualization root or THE root of the file system
    while (nullptr == root && NULLVP != vnode && !vnode_isvroot(vnode))
    {
        int16_t rootIndex = VirtualizationRoots_LookupVnode(vnode, nullptr);
        if (rootIndex >= 0)
        {
            root = &s_virtualizationRoots[rootIndex];
            break;
        }
        else if (rootIndex == RootIndex_ProviderTemporaryDirectory)
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
    
    return root;
}

int16_t VirtualizationRoots_LookupVnodeHierarchical(vnode_t vnode)
{
    int16_t rootIndex = RootIndex_None;
    
    vnode_get(vnode);
    while (NULLVP != vnode && !vnode_isvroot(vnode))
    {
        rootIndex = VirtualizationRoots_LookupVnode(vnode, nullptr);
        if (rootIndex >= 0)
        {
            break;
        }
        else if (rootIndex == RootIndex_ProviderTemporaryDirectory)
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
    
    return rootIndex;
}

int16_t VirtualizationRoots_LookupVnode(vnode_t vnode, vfs_context_t context)
{
    VnodeFsidInode fsidInode = Vnode_GetFsidAndInode(vnode, context);
    uint32_t vid = vnode_vid(vnode);
    
    int16_t rootIndex;
    
    RWLock_AcquireShared(s_rwLock);
    {
        rootIndex = FindRootForVnode_Locked(vnode, vid, fsidInode);
    }
    RWLock_ReleaseShared(s_rwLock);
    
    if (rootIndex == RootIndex_None)
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
                rootIndex = FindRootForVnode_Locked(vnode, vid, fsidInode);
                
                if (rootIndex == RootIndex_None)
                {
                    // Insert new offline root
                    rootIndex = InsertVirtualizationRoot_Locked(nullptr, 0, vnode, vid, fsidInode, path, NULLVP);
                    
                    // TODO: error handling
                    assert(rootIndex >= 0);
                }

            }
            RWLock_ReleaseExclusive(s_rwLock);
        }
    }
    
    return rootIndex;
}

static int16_t FindUnusedIndex_Locked()
{
    for (int16_t i = 0; i < MaxVirtualizationRoots; ++i)
    {
        if (!s_virtualizationRoots[i].inUse)
        {
            return i;
        }
    }
    
    return RootIndex_None;
}

static bool FsidsAreEqual(fsid_t a, fsid_t b)
{
    return a.val[0] == b.val[0] && a.val[1] == b.val[1];
}

static int16_t FindRootForVnode_Locked(vnode_t vnode, uint32_t vid, VnodeFsidInode fileId)
{
    for (int16_t i = 0; i < MaxVirtualizationRoots; ++i)
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
        
        // no need to compare a vid here, as tempDirectoryVNode is always retained or null
        if (rootEntry.tempDirectoryVNode == vnode)
        {
            return RootIndex_ProviderTemporaryDirectory;
        }
        
        if (FsidsAreEqual(rootEntry.rootFsid, fileId.fsid) && rootEntry.rootInode == fileId.inode)
        {
            // root vnode must be stale, update it
            rootEntry.rootVNode = vnode;
            rootEntry.rootVNodeVid = vid;
            return i;
        }
    }
    return RootIndex_None;
}

// Returns negative value if it failed, or inserted index on success
static int16_t InsertVirtualizationRoot_Locked(PrjFSProviderUserClient* userClient, pid_t clientPID, vnode_t vnode, uint32_t vid, VnodeFsidInode persistentIds, const char* path, vnode_t temporaryDirectory)
{
    // New root
    int16_t rootIndex = FindUnusedIndex_Locked();
    
    if (rootIndex >= 0)
    {
        assert(rootIndex < MaxVirtualizationRoots);
        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        
        root->providerUserClient = userClient;
        root->providerPid = clientPID;
        root->inUse = true;
        root->index = rootIndex;

        root->rootVNode = vnode;
        root->rootVNodeVid = vid;
        root->rootFsid = persistentIds.fsid;
        root->rootInode = persistentIds.inode;
        strlcpy(root->path, path, sizeof(root->path));
        
        root->tempDirectoryVNode = temporaryDirectory;
    }
    
    return rootIndex;
}

// Return values:
// 0:        Virtualization root found and successfully registered
// ENOMEM:   Too many virtualization roots.
// ENOTDIR:  Selected virtualization root path does not resolve to a directory.
// EBUSY:    Already a provider for this virtualization root.
// ENOENT,…: Error returned by vnode_lookup.
VirtualizationRootResult VirtualizationRoot_RegisterProviderForPath(PrjFSProviderUserClient* userClient, pid_t clientPID, const char* virtualizationRootPath, const char* providerTemporaryDirectoryPath)
{
    assert(nullptr != virtualizationRootPath);
    assert(nullptr != userClient);
    
    vnode_t virtualizationRootVNode = NULLVP;
    vnode_t temporaryDirectoryVNode = NULLVP;
    vfs_context_t vfsContext = vfs_context_create(nullptr);
    
    int32_t rootIndex = RootIndex_None;
    errno_t err = vnode_lookup(virtualizationRootPath, 0 /* flags */, &virtualizationRootVNode, vfsContext);
    if (0 == err)
    {
        err = vnode_lookup(providerTemporaryDirectoryPath, 0 /* flags */, &temporaryDirectoryVNode, vfsContext);
    }
    
    if (0 == err)
    {
        if (!VirtualizationRoot_VnodeIsOnAllowedFilesystem(virtualizationRootVNode)
            || vnode_mount(virtualizationRootVNode) != vnode_mount(temporaryDirectoryVNode))
        {
            err = ENODEV;
        }
        else if (!vnode_isdir(virtualizationRootVNode) || !vnode_isdir(temporaryDirectoryVNode))
        {
            err = ENOTDIR;
        }
        else
        {
            VnodeFsidInode vnodeIds = Vnode_GetFsidAndInode(virtualizationRootVNode, vfsContext);
            uint32_t rootVid = vnode_vid(virtualizationRootVNode);
            
            RWLock_AcquireExclusive(s_rwLock);
            {
                rootIndex = FindRootForVnode_Locked(virtualizationRootVNode, rootVid, vnodeIds);
                if (rootIndex >= 0)
                {
                    // Reattaching to existing root
                    if (nullptr != s_virtualizationRoots[rootIndex].providerUserClient)
                    {
                        // Only one provider per root
                        err = EBUSY;
                        rootIndex = RootIndex_None;
                    }
                    else
                    {
                        VirtualizationRoot& root = s_virtualizationRoots[rootIndex];
                        root.providerUserClient = userClient;
                        root.providerPid = clientPID;
                        virtualizationRootVNode = NULLVP; // transfer ownership
                        
                        
                        root.tempDirectoryVNode = temporaryDirectoryVNode;
                        temporaryDirectoryVNode = NULLVP; // transfer ownership
                    }
                }
                else if (rootIndex == RootIndex_ProviderTemporaryDirectory)
                {
                    err = EBUSY;
                    rootIndex = RootIndex_None;
                }
                else
                {
                    rootIndex = InsertVirtualizationRoot_Locked(userClient, clientPID, virtualizationRootVNode, rootVid, vnodeIds, virtualizationRootPath, temporaryDirectoryVNode);
                    if (rootIndex >= 0)
                    {
                        assert(rootIndex < MaxVirtualizationRoots);
                        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
                    
                        strlcpy(root->path, virtualizationRootPath, sizeof(root->path));
                        virtualizationRootVNode = NULLVP; // prevent vnode_put later; active provider should hold vnode reference
                        temporaryDirectoryVNode = NULLVP; // inserting transferred ownership, prevents vnode_put later
                        
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
    
    if (NULLVP != temporaryDirectoryVNode)
    {
        vnode_put(temporaryDirectoryVNode);
    }
    
    vfs_context_rele(vfsContext);
    
    return VirtualizationRootResult { err, rootIndex };
}

void ActiveProvider_Disconnect(int32_t rootIndex)
{
    assert(rootIndex >= 0);
    assert(rootIndex <= MaxVirtualizationRoots);

    RWLock_AcquireExclusive(s_rwLock);
    {
        VirtualizationRoot* root = &s_virtualizationRoots[rootIndex];
        assert(nullptr != root->providerUserClient);
        
        assert(NULLVP != root->rootVNode);
        vnode_put(root->rootVNode);
        root->providerPid = 0;
        
        assert(NULLVP != root->tempDirectoryVNode);
        vnode_put(root->tempDirectoryVNode);
        root->tempDirectoryVNode = NULLVP;
        
        root->providerUserClient = nullptr;
    }
    RWLock_ReleaseExclusive(s_rwLock);
}

errno_t ActiveProvider_SendMessage(int32_t rootIndex, const Message message)
{
    assert(rootIndex >= 0);
    assert(rootIndex < MaxVirtualizationRoots);

    PrjFSProviderUserClient* userClient = nullptr;
    
    RWLock_AcquireExclusive(s_rwLock);
    {
        userClient = s_virtualizationRoots[rootIndex].providerUserClient;
        if (nullptr != userClient)
        {
            userClient->retain();
        }
    }
    RWLock_ReleaseExclusive(s_rwLock);
    
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

