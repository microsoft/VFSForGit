#pragma once

#include <sys/kernel_types.h>
#include <sys/_types/_fsid_t.h>
#include "public/FsidInode.h"

struct SizeOrError
{
    size_t size;
    errno_t error;
};

SizeOrError Vnode_ReadXattr(vnode_t _Nonnull vnode, const char* _Nonnull xattrName, void* _Nullable buffer, size_t bufferSize);
FsidInode Vnode_GetFsidAndInode [[nodiscard]] (vnode_t _Nonnull vnode, vfs_context_t _Nonnull context, bool useLinkIDForInode);
