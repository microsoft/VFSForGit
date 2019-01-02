#pragma once

#ifndef KEXT_UNIT_TESTING
#error Don't #include this file in non-testing builds
#endif

#include "VirtualizationRoots.hpp"
#include "VirtualizationRootsPrivate.hpp"

extern uint16_t s_maxVirtualizationRoots;
extern VirtualizationRoot* s_virtualizationRoots;

KEXT_STATIC VirtualizationRootHandle InsertVirtualizationRoot_Locked(PrjFSProviderUserClient* userClient, pid_t clientPID, vnode_t vnode, uint32_t vid, FsidInode persistentIds, const char* path);

