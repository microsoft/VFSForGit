#include "public/PrjFSCommon.h"
#include <sys/kernel_types.h>
#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include "../PrjFSKext/PerformanceTracing.hpp"

#ifndef __cplusplus
#error None of the kext code is set up for being called from C or Objective-C; change the including file to C++ or Objective-C++
#endif

#ifndef KEXT_UNIT_TESTING
#error This class should only be called for unit tests
#endif

KEXT_STATIC_INLINE bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit);
KEXT_STATIC_INLINE bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask);
KEXT_STATIC_INLINE bool TryGetFileIsFlaggedAsInRoot(vnode_t vnode, vfs_context_t context, bool* flaggedInRoot);
KEXT_STATIC bool IsFileSystemCrawler(const char* procname);
KEXT_STATIC bool ShouldIgnoreVnodeType(vtype vnodeType, vnode_t vnode);
KEXT_STATIC int HandleVnodeOperation(
    kauth_cred_t    credential,
    void*           idata,
    kauth_action_t  action,
    uintptr_t       arg0,
    uintptr_t       arg1,
    uintptr_t       arg2,
    uintptr_t       arg3);
KEXT_STATIC bool ShouldHandleVnodeOpEvent(
    // In params:
    PerfTracer* perfTracer,
    vfs_context_t context,
    const vnode_t vnode,
    kauth_action_t action,

    // Out params:
    vtype* vnodeType,
    uint32_t* vnodeFileFlags,
    int* pid,
    char procname[MAXCOMLEN + 1],
    int* kauthResult,
    int* kauthError);
