#include "public/PrjFSCommon.h"
#include <sys/kernel_types.h>

#ifndef __cplusplus
#error None of the kext code is set up for being called from C or Objective-C; change the including file to C++ or Objective-C++
#endif

KEXT_TESTABLE_STATIC_INLINE bool FileFlagsBitIsSet(uint32_t fileFlags, uint32_t bit);
KEXT_TESTABLE_STATIC_INLINE bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask);
KEXT_TESTABLE_STATIC bool IsFileSystemCrawler(const char* procname);
