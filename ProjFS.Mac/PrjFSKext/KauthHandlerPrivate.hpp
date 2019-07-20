#pragma once

#include "kernel-header-wrappers/kauth.h"

// Define missing symbol when building with 10.13 SDK/Xcode 9
#ifndef KAUTH_FILEOP_WILL_RENAME
#define KAUTH_FILEOP_WILL_RENAME 8
#endif
