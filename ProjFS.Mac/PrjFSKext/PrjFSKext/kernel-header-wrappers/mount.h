#pragma once

// <sys/mount> triggers various header doc related warnings
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdocumentation"
#include <sys/mount.h>
#pragma clang diagnostic pop
