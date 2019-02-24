#pragma once

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
