#pragma once

#include <sys/kernel_types.h>

struct MountPoint
{
    mount_t  mountPoint;
    uint32_t authCacheDisableCount;
    int      savedAuthCacheTTL;
};

static constexpr size_t MaxUsedMountPoints = 4;
