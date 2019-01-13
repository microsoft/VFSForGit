#pragma once

#ifndef KEXT_UNIT_TESTING
#error Don't #include this file in non-testing builds
#endif

#include "FileSystemMountPointsPrivate.hpp"
#include "public/PrjFSCommon.h"

KEXT_STATIC ssize_t FindMountPoint_Locked(mount_t mountPoint);
KEXT_STATIC_INLINE ssize_t FindEmptyMountPointSlot_Locked();
KEXT_STATIC ssize_t TryFindOrInsertMountPoint_Locked(mount_t mountPoint);

#ifdef KEXT_UNIT_TESTING // otherwise this is a static
extern MountPoint s_usedMountPoints[MaxUsedMountPoints];
#endif
