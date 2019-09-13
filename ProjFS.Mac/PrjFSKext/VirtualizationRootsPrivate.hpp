#pragma once

struct VirtualizationRoot
{
    bool                        inUse;
    // If this is a nullptr, there is no active provider for this virtualization root (offline root)
    PrjFSProviderUserClient*    providerUserClient;
    pid_t                       providerPid;
    // For an active root, this is retained (vnode_get), for an offline one, it is not, so it may be stale (check the vid)
    vnode_t                     rootVNode;
    uint32_t                    rootVNodeVid;
    
    // Mount point ID + persistent, on-disk ID for the root directory, so we can
    // identify it if the vnode of an offline root gets recycled.
    fsid_t                      rootFsid;
    uint64_t                    rootInode;
    
    // Only contains a valid path for online roots (active provider)
    char                        path[PrjFSMaxPath];
};

#if defined(KEXT_UNIT_TESTING) && !defined(TESTABLE_KEXT_TARGET) // Building unit tests
#include <type_traits>
static_assert(std::is_trivially_copyable<VirtualizationRoot>::value, "The array of VirtualizationRoot objects is resized using memcpy");
#endif
