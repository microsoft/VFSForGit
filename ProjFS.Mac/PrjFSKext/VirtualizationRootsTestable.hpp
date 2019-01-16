#pragma once

#ifndef KEXT_UNIT_TESTING
#error Don't #include this file in non-testing builds
#endif

#include "VirtualizationRootsPrivate.hpp"

extern uint16_t s_maxVirtualizationRoots;
extern VirtualizationRoot* s_virtualizationRoots;

KEXT_STATIC VirtualizationRootHandle FindOrInsertVirtualizationRoot_LockedMayUnlock(vnode_t virtualizationRootVNode, uint32_t rootVid, FsidInode persistentIds, const char* path);

