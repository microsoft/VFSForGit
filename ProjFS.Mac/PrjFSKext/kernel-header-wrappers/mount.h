#pragma once

#if defined(KEXT_UNIT_TESTING) && !defined(TESTABLE_KEXT_TARGET) // Building unit tests

typedef uint32_t user32_addr_t;
typedef uint32_t user32_size_t;
#include <Kernel/sys/mount.h>

#else // Building the kext or the testing version of it

// <sys/mount.h> triggers various header doc related warnings
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdocumentation"
#include <sys/mount.h>
#pragma clang diagnostic pop

#endif
