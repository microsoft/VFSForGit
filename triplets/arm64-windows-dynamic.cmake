set(VCPKG_TARGET_ARCHITECTURE arm64)
# Dynamic linkage: produces git2.dll for non-AOT projects (tests) that use
# runtime P/Invoke. AOT projects use the static triplet instead.
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
