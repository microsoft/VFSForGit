#include <sys/kernel_types.h>

#ifndef __cplusplus
#error None of the kext code is set up for being called from C or Objective-C; change the including file to C++ or Objective-C++
#endif

#ifdef KEXT_UNIT_TESTING
#define KEXT_TESTABLE_STATIC_INLINE
#else
#define KEXT_TESTABLE_STATIC_INLINE static inline
#endif
KEXT_TESTABLE_STATIC_INLINE bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask);
