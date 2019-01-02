#pragma once

struct VnodeCacheEntry
{
    vnode_t vnode;
    uint32_t vid;   // vnode generation number
    VirtualizationRootHandle virtualizationRoot;
};

// Allow cache the cache to use between 4 MB and 64 MB of memory (assuming 16 bytes per VnodeCacheEntry)
KEXT_STATIC const uint32_t AllowedPow2CacheCapacities[] =
    {
        0x040000,
        0x080000,
        0x100000,
        0x200000,
        0x400000,
    };
