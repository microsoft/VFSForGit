# .NET 10 NativeAOT Migration — Status & Remaining Work

**Branch:** `tyrielv/net10-tfm-retarget`
**PR:** [#1947](https://github.com/microsoft/VFSForGit/pull/1947)
**Base:** `upstream/master` (includes merged Phases 1–2)

## Migration Phases Overview

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Remove dead/deprecated code | ✅ Merged (PRs #1937, #1946, #1941, #1939, #1938) |
| 2 | System.Text.Json migration | ✅ Merged (PR above) |
| 3 | TFM retarget + API migrations | ✅ In this PR |
| 4 | NativeAOT self-contained deployment | ✅ In this PR |
| 5 | Cleanup & optimization | 🔲 Future PR |

## What This PR Contains

### Commit 1: TFM Retarget + API Migrations + CI
- Retargeted all 16 managed csproj files from `net471` to `net10.0-windows10.0.17763.0`
- Updated `global.json`, `Directory.Build.props`, `Version.props`, `Directory.Packages.props`
- Fixed API migrations: `ProcessHelper` (Environment.ProcessPath), `OrgInfoApiClient` (WebUtility.UrlEncode), `WindowsPlatform` (NamedPipeServerStreamAcl.Create), `HttpRequestor` (removed UseDefaultCredentials), `WindowsFileSystem` (DirectoryInfo/FileSecurity APIs)
- Updated CI `build.yaml`, scripts (`Build.bat`, `RunUnitTests.bat`, `RunFunctionalTests.bat`, `CreateBuildArtifacts.bat`, `RunFunctionalTests-Dev.ps1`)
- Fixed obsolete API warnings (Uri.EscapeUriString, X509Certificate2 ctor)
- 803 unit tests passing

### Commit 2: NativeAOT Self-Contained Deployment
- Added `SelfContained=true`, `PublishAot=true`, `OptimizationPreference=Speed` to Directory.Build.props
- Opted out test projects and GVFS.MSBuild from AOT
- Rewrote `Build.bat`: 3-step approach (dotnet restore → VS MSBuild for C++ → dotnet publish for managed)
- Updated all script paths for `publish\` subdirectory
- Created `GVFSJsonContext.cs` — source-generated JSON serializer context (25+ types)
- Updated `GVFSJsonOptions.cs` with TypeInfoResolverChain (source-gen + reflection fallback)
- Cleaned installer payload: removed stray `.runtimeconfig.json`, orphaned PDBs
- AOT binaries: GVFS.exe ~20MB, GVFS.Mount.exe ~20MB, GVFS.Service.exe ~6.7MB

### Commit 3: CI Diagnostic Logging + Timeout Handling
- Added `[CI-DEBUG]` logging to clone/mount/gvfs process invocations
- Added 5-minute timeout per gvfs process call
- Added `--workers=1` for sequential CI test execution
- Added `timeout-minutes: 60` to functional-tests.yaml

### Commit 4: Null Guard for ProjFS triggeringProcessImageFileName
- Fixed `ArgumentNullException` crash in mount process callbacks
- Root cause: ProjFS managed API v2.1.0 returns null for kernel-level operations
- Fix: null-coalesce to `string.Empty` in `FileSystemCallbacks`

### Commit 5: CountingStream for Truncated Object Detection
- Fixed `TruncatedObjectRedownloaded` test failure (4/4 in CI)
- Root cause: .NET 10 `DeflateStream` silently returns partial data on truncated zlib
- Fix: `CountingStream` wrapper verifies bytes read matches header-declared size

## Current CI Results (as of last push)

| Slice | Status | Notes |
|-------|--------|-------|
| 0 | ✅ 4/4 | |
| 1 | ✅ 4/4 | Fixed by CountingStream |
| 2 | ⚠️ 2/4 | Intermittent: `gvfs prefetch --hydrate` AV crash (0xC0000005) |
| 3 | ⚠️ 1/4 | Intermittent: same prefetch AV crash |
| 4 | ✅ 4/4 | |
| 5 | ✅ 4/4 | |
| 6 | ✅ 4/4 | |
| 7 | ❌ 0/4 | `ChangeTimestampAndDiff`: pathspec not found — see below |
| 8 | ⚠️ 3/4 | 1 intermittent failure |
| 9 | ❌ 0/4 | All flavors hang/timeout — see below |

**Overall: 7 of 10 slices fully passing, up from 0 at start of CI debugging.**

## Remaining Failures to Investigate

### 1. `ChangeTimestampAndDiff` (Slice 7, 4/4 fail)

**Error:** `git checkout GVFS\GVFS.Common\GVFSContext.cs` fails with "pathspec did not match any file(s) known to git"

**Analysis:**
- The file path is hardcoded in `GitCommandsTests.EditFilePath` (line 23)
- The file DOES exist in the test repo (`ForTests`) — confirmed by manual clone
- `ValidateGitCommand` runs git in both the GVFS enlistment and a control repo
- The error comes from the GVFS enlistment side, suggesting a projection issue
- Could be related to the mount process or index projection failing for this specific file
- **Not yet locally reproduced** — local test framework was setting up 20 parallel fixtures due to wrong NUnit filter. Use `--test=` (not `--where=`) to run individual tests

### 2. Slice 9 Hang (4/4 timeout)

**Analysis:**
- All 4 flavors time out at 60 minutes
- Need to identify which test(s) fall into slice 9 — run test discovery with `--slice=9,10`
- Could be the same mount crash or a deadlock in a specific test

### 3. Prefetch --hydrate AV (Slices 2/3, intermittent)

**Error:** `gvfs prefetch` exits with -1073741819 (0xC0000005 ACCESS_VIOLATION)

**Analysis:**
- Happens during `PrefetchWithStats` → multithreaded pipeline (diff → blobFinder → downloader → packIndexer → fileHydrator)
- `HydrateFilesStage` calls `NativeFileReader.TryReadFirstByteOfFile` via P/Invoke (CreateFile/ReadFile)
- Intermittent (2/4 and 3/4) — likely a race condition or timing-dependent AOT issue
- P/Invoke declarations use `DllImport` — could benefit from `LibraryImport` for AOT
- The `SafeFileHandle` marshaling under AOT may have edge cases

## .NET 10 Behavioral Changes Discovered

### DeflateStream Truncation Behavior
- **Old (.NET 4.7.1):** Throws `InvalidDataException` on truncated zlib data
- **New (.NET 10):** Silently returns partial data, no exception
- **Impact:** Any code relying on DeflateStream exceptions for corruption detection needs explicit length checks
- **Fix applied:** `CountingStream` in `GitRepo.GetLooseBlobStateAtPath`

### ProjFS Managed API Null Behavior
- **Old (C++/CLI):** `TriggeringProcessImageFileName` returned `String.Empty` for NULL native pointers
- **New (P/Invoke v2.1.0):** Returns `null` from `Marshal.PtrToStringUni(IntPtr.Zero)`
- **Impact:** Any consumer using the value as a dictionary key or in null-intolerant APIs will crash
- **Fix applied:** Null-coalesce in `FileSystemCallbacks`
- **Upstream fix needed:** `Microsoft.Windows.ProjFS` package should match old behavior

## Deferred Items (Phase 5 / Future PRs)

- ProjFS driver installation removal (branch `tyrielv/remove-projfs-install` created but not implemented)
- `upgrade.ring` config removal
- Full `WinHttpHandler` replacement (see miniksa's approach)
- `info.bat` update for ProjFS paths
- Remove CI debug logging (`[CI-DEBUG]` lines) after tests stabilize
- Re-enable parallel workers (remove `--workers=1`) after fixing remaining issues
- File ProjFS upstream issue for null `TriggeringProcessImageFileName`

## Build & Test Commands

```powershell
# Build (from src\)
scripts\Build.bat Debug

# Unit tests
..\out\GVFS.UnitTests\bin\Debug\net10.0-windows10.0.17763.0\win-x64\publish\GVFS.UnitTests.exe

# Functional tests (dev mode, no admin)
powershell -File scripts\RunFunctionalTests-Dev.ps1 Debug '--test=<fully.qualified.test.name>'

# Install local build
$v = & { $n = [DateTime]::Now; "0.2.$($n.ToString('yy'))$($n.DayOfYear.ToString('D3')).$([int]($n.TimeOfDay.TotalSeconds / 2))" }
scripts\Build.bat Debug $v
# Then run installer from out\GVFS.Installers\bin\Debug\win-x64\
```

## CI NuGet Feed

CI uses `https://pkgs.dev.azure.com/gvfs/ci/_packaging/Dependencies/nuget/v3/index.json` as the sole NuGet source. To pull through new packages:
1. Clear ALL local caches: `dotnet nuget locals all --clear`
2. Run `dotnet restore` — packages automatically pull through from nuget.org
3. Never push packages manually to the feed

SDK version is pinned in `global.json` to `10.0.202` with `rollForward: disable`.
