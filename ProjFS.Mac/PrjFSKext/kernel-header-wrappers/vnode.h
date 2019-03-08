#pragma once

#if defined(KEXT_UNIT_TESTING) && !defined(TESTABLE_KEXT_TARGET) // Building unit tests

#include <time.h> // vnode.h below references struct timespec
// Definitions to make the kernel vnode.h compile in user space
typedef struct ipc_port* ipc_port_t;
enum uio_rw {};
enum uio_seg {};

#include <Kernel/sys/vnode.h>

#else // Building the kext or the testing version of it

// <sys/vnode.h> triggers various header doc related warnings
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdocumentation"
#include <sys/vnode.h>
#pragma clang diagnostic pop

#endif
