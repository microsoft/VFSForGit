# libgit2 vcpkg Overlay Port

This overlay replaces the default vcpkg `libgit2` port to pin a specific
version and optionally carry patches ahead of upstream releases.

## Current version

**libgit2 v1.9.7** — latest v1.9.x release (2026-08-13). This is ahead of the
official vcpkg port, which pins v1.9.3 at the baseline recorded in
`vcpkg-configuration.json`. v1.9.x is the last of the libgit2 1.x line.

## Patches

- `dependencies.diff` — adjusts CMake dependency resolution for vcpkg
  (copied from official vcpkg port, required for PCRE discovery)
- `non-elevated-admin-owner.diff` — support non-elevated admin user
  ownership check on Windows ([libgit2/libgit2#7200](https://github.com/libgit2/libgit2/pull/7200)).
  Allows non-elevated processes run by Administrators group members to be
  considered the owner of repos owned by that group. Related to
  [libgit2/libgit2#6279](https://github.com/libgit2/libgit2/issues/6279).
  This overlay carries the version that was **merged to libgit2 `main`**
  (2026-06-07), which fixes handle-leak, static-handle, and
  uninitialized-token bugs present in the original PR #7200 diff. See the
  provenance table below. The patch is **not yet in any libgit2 release**
  (latest is v1.9.7; expected to ship upstream in v2.0, no ETA), so it
  remains necessary until the pinned port moves to a libgit2 that includes it.
- `safe-directory-icase.diff` — compare `safe.directory` allowlist entries
  against the repository path case-insensitively on Windows
  ([libgit2/libgit2#7037](https://github.com/libgit2/libgit2/issues/7037)).
  Git canonicalizes both paths with `real_pathdup` before comparing, so an
  entry like `c:/repo` matches a repository recorded as `C:/repo` (a
  drive-letter case mismatch is the common case). libgit2 used a
  case-sensitive `strcmp`, so the repository failed to open with a spurious
  `GIT_EOWNER` ownership error. The fix switches the comparison to
  `STRCMP_CASESELECT` gated on `GIT_WIN32`; POSIX stays case-sensitive. The
  upstream change is on fork branch `tyrielv/safe-directory-drive-case`
  (commit `6074349`) with a PR pending against libgit2/libgit2 `main`. The
  patch is **not yet in any libgit2 release**, so it remains necessary until
  the pinned port moves to a libgit2 that includes it.

Additional patches can be added to the `PATCHES` list in `portfile.cmake`
to apply fixes that haven't shipped in an official libgit2 release yet.

## File provenance

All files were copied from the
[official vcpkg libgit2 port](https://github.com/microsoft/vcpkg/tree/master/ports/libgit2)
and then modified as noted below.

| File | Source | Changes |
|------|--------|---------|
| `vcpkg.json` | Official vcpkg port | Version bumped to v1.9.7 (`version-semver`); otherwise unchanged. |
| `dependencies.diff` | Official vcpkg port | Unchanged |
| `portfile.cmake` | Official vcpkg port | Removed patches not needed for MSVC x64: `c-standard.diff` (C99 inline keyword — MSVC handles natively), `cli-include-dirs.diff` (CLI tool build — we set `BUILD_CLI=OFF`), `mingw-winhttp.diff` (MinGW only) |
| `non-elevated-admin-owner.diff` | libgit2 `main` (commits `cc477ee` + `f9f36a6` + `44c05e5`, merge `e805a16`) | Matches the version merged to `main` on 2026-06-07, not the original [PR #7200](https://github.com/libgit2/libgit2/pull/7200) diff. Maintainer follow-ups fixed a `linked_token` handle leak, replaced a non-thread-safe `static HANDLE` with a per-call local, corrected the `*linked_token = NULL` failure path (with `CloseHandle`), and hoisted `current_user_sid()` so `linked_token` is always populated before both branches use it. |
| `safe-directory-icase.diff` | libgit2 fork `tyrielv/safe-directory-drive-case` (commit `6074349`) | Rebased onto v1.9.7. The upstream commit targets `main`, whose `validate_ownership_cb` has an extra `%(prefix)`/`is_prefix` branch that v1.9.7 lacks, so this overlay carries only the single-comparison hunk adapted to v1.9.7 (`STRCMP_CASESELECT` in place of `strcmp`). The upstream commit's Windows-only tests in `tests/libgit2/repo/open.c` are omitted because the overlay builds with `BUILD_TESTS=OFF`. |
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
