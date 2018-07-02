#pragma once

#include <sys/kernel_types.h>
#include <sys/_types/_fsid_t.h>

struct SizeOrError
{
    size_t size;
    errno_t error;
};

SizeOrError Vnode_ReadXattr(vnode_t vnode, const char* xattrName, void* buffer, size_t bufferSize, vfs_context_t context);

struct VnodeFsidInode
{
    fsid_t fsid;
    uint64_t inode;
};
VnodeFsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t context);
