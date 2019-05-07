#pragma once

#if defined(KEXT_UNIT_TESTING) && !defined(TESTABLE_KEXT_TARGET) // Building unit tests

#include <sys/kernel_types.h>
typedef struct __lck_grp__ lck_grp_t;
#include <Kernel/sys/kauth.h>

#else

#include <sys/kauth.h>

#endif
