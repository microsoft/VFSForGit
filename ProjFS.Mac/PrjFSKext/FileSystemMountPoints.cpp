#include "FileSystemMountPoints.hpp"
#include "kernel-header-wrappers/mount.h"

void MountPoint_DisableAuthCache(mount_t mountPoint)
{
    vfs_setauthcache_ttl(mountPoint, 0);
}

void MountPoint_RestoreAuthCache(mount_t mountPoint)
{
}
