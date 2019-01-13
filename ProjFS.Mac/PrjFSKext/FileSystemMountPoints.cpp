#include "FileSystemMountPointsPrivate.hpp"
#include "FileSystemMountPoints.hpp"
#include "kernel-header-wrappers/mount.h"
#include "Locks.hpp"
#include "KextLog.hpp"
#include <kern/assert.h>

#ifdef KEXT_UNIT_TESTING
#include "FileSystemMountPointsTestable.hpp"
#endif

static Mutex s_mountPointsMutex;
KEXT_STATIC MountPoint s_usedMountPoints[MaxUsedMountPoints];
static bool s_reuseMountPointSlots;

bool MountPoint_Init()
{
    s_reuseMountPointSlots = true;
    s_mountPointsMutex = Mutex_Alloc();
    if (!Mutex_IsValid(s_mountPointsMutex))
    {
        return false;
    }
    
    return true;
}

void MountPoint_Cleanup()
{
    if (Mutex_IsValid(s_mountPointsMutex))
    {
        Mutex_FreeMemory(&s_mountPointsMutex);
    }
    
    for (size_t i = 0; i < MaxUsedMountPoints; ++i)
    {
        // by the time this function is called, any provider user clients
        // should have been terminated & deregistered
        assert(s_usedMountPoints[i].authCacheDisableCount == 0);
        assertf(
            !s_reuseMountPointSlots || s_usedMountPoints[i].mountPoint == nullptr,
            "If reusing slots is still allowed, they should all have been reset to the empty state by now; slot reuse allowed: %s, mount point registered in slot %lu: %p",
            s_reuseMountPointSlots ? "yes" : "no",
            i,
            KextLog_Unslide(s_usedMountPoints[i].mountPoint));
    }
}

KEXT_STATIC ssize_t FindMountPoint_Locked(mount_t mountPoint)
{
    for (size_t i = 0; i < MaxUsedMountPoints; ++i)
    {
        if (s_usedMountPoints[i].mountPoint == mountPoint)
        {
            return i;
        }
    }
    
    return -1;
}

KEXT_STATIC_INLINE ssize_t FindEmptyMountPointSlot_Locked()
{
    return FindMountPoint_Locked(nullptr);
}

KEXT_STATIC ssize_t TryFindOrInsertMountPoint_Locked(mount_t mountPoint)
{
    ssize_t mountPointIndex = FindMountPoint_Locked(mountPoint);
    if (mountPointIndex < 0)
    {
        mountPointIndex = FindEmptyMountPointSlot_Locked();
        if (mountPointIndex >= 0)
        {
            s_usedMountPoints[mountPointIndex].mountPoint = mountPoint;
            assert(s_usedMountPoints[mountPointIndex].authCacheDisableCount == 0);
        }
    }
    
    return mountPointIndex;
}

void MountPoint_DisableAuthCache(mount_t mountPoint)
{
    Mutex_Acquire(s_mountPointsMutex);
    {
        ssize_t mountPointIndex = TryFindOrInsertMountPoint_Locked(mountPoint);
        if (mountPointIndex < 0)
        {
            // No space in array, just disable auth cache and don't save previous state
            KextLog_Info("MountPoint_DisableAuthCache: No space to save auth cache state for mount point '%s', restoring will not be possible.", vfs_statfs(mountPoint)->f_mntonname);
            vfs_setauthcache_ttl(mountPoint, 0);
            // No longer allow reuse of slots once all are used; allowing it would
            // open the possibility of miscounting "restore" operations, thus
            // restoring the auth cache TTL despite mount points being in use.
            s_reuseMountPointSlots = false;
        }
        else
        {
            MountPoint* usedMountPoint = &s_usedMountPoints[mountPointIndex];
            if (usedMountPoint->authCacheDisableCount == 0)
            {
                // New entry, save state and disable cache
                usedMountPoint->savedAuthCacheTTL = vfs_authcache_ttl(mountPoint);
                usedMountPoint->authCacheDisableCount = 1;
                vfs_setauthcache_ttl(mountPoint, 0);
            }
            else
            {
                ++usedMountPoint->authCacheDisableCount;
            }
        }
        
    }
    Mutex_Release(s_mountPointsMutex);
}

void MountPoint_RestoreAuthCache(mount_t mountPoint)
{
    Mutex_Acquire(s_mountPointsMutex);
    {
        ssize_t mountPointIndex = FindMountPoint_Locked(mountPoint);
        if (mountPointIndex < 0)
        {
            KextLog_Info("MountPoint_RestoreAuthCache: previous auth cache state for mount point '%s' not found, can't restore.", vfs_statfs(mountPoint)->f_mntonname);
        }
        else
        {
            MountPoint* usedMountPoint = &s_usedMountPoints[mountPointIndex];
            usedMountPoint->authCacheDisableCount--;
            if (0 == usedMountPoint->authCacheDisableCount)
            {
                int savedTTL = usedMountPoint->savedAuthCacheTTL;
                // vfs_clearauthcache_ttl(mount) has a slightly different effect in the kernel than
                // vfs_setauthcache_ttl(mount, CACHED_RIGHT_INFINITE_TTL) so make sure to call the
                // appropriate one when resetting to the original state.
                if (savedTTL == CACHED_RIGHT_INFINITE_TTL)
                {
                    KextLog_Info("DecrementAuthCacheDisableCount: resetting mount point '%s' to default auth cache behaviour",
                        vfs_statfs(mountPoint)->f_mntonname);
                    vfs_clearauthcache_ttl(mountPoint);
                }
                else
                {
                    KextLog_Info("DecrementAuthCacheDisableCount: resetting mount point '%s' TTL to %d",
                        vfs_statfs(mountPoint)->f_mntonname, savedTTL);
                    vfs_setauthcache_ttl(mountPoint, usedMountPoint->savedAuthCacheTTL);
                }
                
                if (s_reuseMountPointSlots)
                {
                    usedMountPoint->mountPoint = nullptr;
                }
            }
        }
    }
    Mutex_Release(s_mountPointsMutex);
}
