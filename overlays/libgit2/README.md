# libgit2 vcpkg Overlay Port

This overlay replaces the default vcpkg `libgit2` port to pin a specific
version and optionally carry patches ahead of upstream releases.

## Current version

**libgit2 v1.9.3** — includes C4703 warning fixes (libgit2/libgit2#7154)
that were missing from v1.9.1.

## Patches

- `dependencies.diff` — adjusts CMake dependency resolution for vcpkg
  (copied from official vcpkg port, required for PCRE discovery)
- `non-elevated-admin-owner.diff` — support non-elevated admin user
  ownership check on Windows ([libgit2/libgit2#7200](https://github.com/libgit2/libgit2/pull/7200)).
  Allows non-elevated processes run by Administrators group members to be
  considered the owner of repos owned by that group. Related to
  [libgit2/libgit2#6279](https://github.com/libgit2/libgit2/issues/6279).

Additional patches can be added to the `PATCHES` list in `portfile.cmake`
to apply fixes that haven't shipped in an official libgit2 release yet.

## File provenance

All files were copied from the
[official vcpkg libgit2 port](https://github.com/microsoft/vcpkg/tree/master/ports/libgit2)
and then modified as noted below.

| File | Source | Changes |
|------|--------|---------|
| `vcpkg.json` | Official vcpkg port | Unchanged |
| `dependencies.diff` | Official vcpkg port | Unchanged |
| `portfile.cmake` | Official vcpkg port | Removed patches not needed for MSVC x64: `c-standard.diff` (C99 inline keyword — MSVC handles natively), `cli-include-dirs.diff` (CLI tool build — we set `BUILD_CLI=OFF`), `mingw-winhttp.diff` (MinGW only) |
| `non-elevated-admin-owner.diff` | [libgit2/libgit2#7200](https://github.com/libgit2/libgit2/pull/7200) | PR diff, verbatim |
| `README.md` | New | VFSForGit-specific documentation |

When updating to a new libgit2 version, compare these files against the
official vcpkg port to pick up any new patches or portfile changes.

## How overlays work

A vcpkg overlay port **completely replaces** the official port — it doesn't
layer on top. When `--overlay-ports=overlays` is specified, vcpkg uses this
directory's `portfile.cmake` and `vcpkg.json` instead of the registry's.

## Usage

```
vcpkg install --triplet x64-windows-static-aot ^
  --overlay-triplets=triplets --overlay-ports=overlays
```
