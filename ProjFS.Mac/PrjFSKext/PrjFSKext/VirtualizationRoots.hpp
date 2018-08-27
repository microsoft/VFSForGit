#pragma once

#include "PrjFSClasses.hpp"
#include "kernel-header-wrappers/vnode.h"

struct VirtualizationRoot
{
    bool                        inUse;
    // If this is a nullptr, there is no active provider for this virtualization root (offline root)
    PrjFSProviderUserClient*    providerUserClient;
    int                         providerPid;
    // For an active root, this is retained (vnode_get), for an offline one, it is not, so it may be stale (check the vid)
    vnode_t                     rootVNode;
    uint32_t                    rootVNodeVid;
    
    // Active providers register a temporary staging directory where placeholder
    // files and directories are prepared, and where events should normally be ignored.
    vnode_t                     tempDirectoryVNode; // retained if not nullptr (vnode_get)
    
    // Mount point ID + persistent, on-disk ID for the root directory, so we can
    // identify it if the vnode of an offline root gets recycled.
    fsid_t                      rootFsid;
    uint64_t                    rootInode;
    
    // TODO(Mac): this should eventually be entirely diagnostic and not used for decisions
    char                        path[PrjFSMaxPath];

    int32_t                     index;
};

// Zero and positive values indicate an index for a valid virtualization
// root. Other values have special meanings:
enum VirtualizationRootSpecialIndex : int16_t
{
    // Not in a virtualization root.
    RootIndex_None                       = -1,
    // Root/non-root state not known. Useful reset value for invalidating cached state.
    RootIndex_Indeterminate              = -2,
    // Vnode is not in a virtualization root, but below a provider's registered temp directory
    RootIndex_ProviderTemporaryDirectory = -3,
};

kern_return_t VirtualizationRoots_Init(void);
kern_return_t VirtualizationRoots_Cleanup(void);

VirtualizationRoot* VirtualizationRoots_FindForVnode(vnode_t vnode);

struct VirtualizationRootResult
{
    errno_t error;
    int32_t rootIndex;
};

VirtualizationRootResult VirtualizationRoot_RegisterProviderForPath(
    PrjFSProviderUserClient* userClient,
    pid_t clientPID,
    const char* virtualizationRootPath,
    const char* providerTemporaryDirectoryPath);

void ActiveProvider_Disconnect(int32_t rootIndex);

struct Message;
errno_t ActiveProvider_SendMessage(int32_t rootIndex, const Message message);
bool VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode_t vnode);

int16_t VirtualizationRoots_LookupVnode(vnode_t vnode, vfs_context_t context);
