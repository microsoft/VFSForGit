//
//  VnodeUtilities.cpp
//  PrjFSKext
//
//  Created by Phil Dennis-Jordan on 15/05/18.
//  Copyright © 2018 GVFS. All rights reserved.
//

#include "VnodeUtilities.hpp"
#include "kernel-header-wrappers/vnode.h"
#include "kernel-header-wrappers/mount.h"
#include <sys/xattr.h>

extern "C" int mac_vnop_getxattr(struct vnode *, const char *, char *, size_t, size_t *);

VnodeFsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t context)
{
    vnode_attr attrs;
    VATTR_INIT(&attrs);
    // TODO: check this is correct for hardlinked files
    VATTR_WANTED(&attrs, va_fileid);

    vnode_getattr(vnode, &attrs, context);
    
    vfsstatfs* statfs = vfs_statfs(vnode_mount(vnode));
    return { statfs->f_fsid, attrs.va_fileid };
}

SizeOrError Vnode_ReadXattr(vnode_t vnode, const char* xattrName, void* buffer, size_t bufferSize, vfs_context_t context)
{
    size_t actualSize = bufferSize;
    errno_t error = mac_vnop_getxattr(vnode, xattrName, static_cast<char*>(buffer), bufferSize, &actualSize);
    return SizeOrError { actualSize, error };
}
