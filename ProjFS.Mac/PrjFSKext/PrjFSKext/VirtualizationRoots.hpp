#pragma once

#include "FsidInode.h"
#include "PrjFSClasses.hpp"
#include "kernel-header-wrappers/vnode.h"

typedef int16_t VirtualizationRootHandle;

// Zero and positive values indicate a handle for a valid virtualization
// root. Other values have special meanings:
enum VirtualizationRootSpecialHandle : VirtualizationRootHandle
{
    // Not in a virtualization root.
    RootHandle_None                       = -1,
    // Root/non-root state not known. Useful reset value for invalidating cached state.
    RootHandle_Indeterminate              = -2,
    // Vnode is not in a virtualization root, but below a provider's registered temp directory
    RootHandle_ProviderTemporaryDirectory = -3,
};

kern_return_t VirtualizationRoots_Init(void);
kern_return_t VirtualizationRoots_Cleanup(void);

VirtualizationRootHandle VirtualizationRoot_FindForVnode(vnode_t vnode, const FsidInode& vnodeFsidInode);

struct VirtualizationRootResult
{
    errno_t error;
    VirtualizationRootHandle root;
};
VirtualizationRootResult VirtualizationRoot_RegisterProviderForPath(PrjFSProviderUserClient* userClient, pid_t clientPID, const char* virtualizationRootPath);
void ActiveProvider_Disconnect(VirtualizationRootHandle rootHandle);

struct Message;
errno_t ActiveProvider_SendMessage(VirtualizationRootHandle rootHandle, const Message message);
bool VirtualizationRoot_VnodeIsOnAllowedFilesystem(vnode_t vnode);
bool VirtualizationRoot_IsOnline(VirtualizationRootHandle rootHandle);
bool VirtualizationRoot_PIDMatchesProvider(VirtualizationRootHandle rootHandle, pid_t pid);
bool VirtualizationRoot_IsValidRootHandle(VirtualizationRootHandle rootHandle);
const char* VirtualizationRoot_GetRootRelativePath(VirtualizationRootHandle rootHandle, const char* path);
