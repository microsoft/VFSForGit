set(VCPKG_TARGET_ARCHITECTURE x64)
# Static linkage: libgit2 and its dependencies (pcre, zlib) are compiled into
# the consuming binary. This eliminates "DLL missing" crashes for the native
# git2 library.
#
# Licensing notes:
#   libgit2 — GPLv2 with linking exception (see COPYING in libgit2 repo), which
#             explicitly permits static linking without imposing GPL on the consumer.
#   pcre    — BSD license (permissive, no static-linking restrictions).
#   zlib    — zlib license (permissive, no static-linking restrictions).
set(VCPKG_CRT_LINKAGE static)
set(VCPKG_LIBRARY_LINKAGE static)
