# Design: VFSForGit .NET 10 + NativeAOT Migration

## Summary

This document describes the phased migration of VFSForGit from .NET Framework 4.7.1
to .NET 10 with NativeAOT compilation. The migration was executed in six incremental
phases, each independently reviewable and testable.

## Motivation

VFSForGit is a performance-critical system component — every `gvfs` CLI invocation,
every mount operation, every file access through ProjFS must be fast. The .NET Framework
4.7.1 baseline has inherent limitations:

- **JIT startup cost** — 50+ ms for every CLI invocation
- **No NativeAOT** — Cannot compile to native code
- **Aging dependencies** — Newtonsoft.Json, CommandLineParser with known CVEs
- **C++/CLI dependency** — Mixed-mode ProjFS wrapper blocks AOT
- **Large deployment** — Requires .NET Framework runtime on target machines

## Phase 1: TFM Retarget

**Branch:** `user/miniksa/net10-migration`

Changed all 18 .csproj files from `net471` to `net10.0-windows10.0.17763.0`.
Centralized build settings in `Directory.Build.props`.

**Risk:** Low — mostly mechanical changes.
**Tests:** 578/578 unit tests pass.

## Phase 2: Package Updates

Updated all NuGet dependencies to .NET 10 compatible versions.

| Package | Before | After |
|---------|--------|-------|
| ESENT Collections | 3.2.0 (net48) | 3.2.0 (net8.0) |
| NUnitLite | 3.11.0 | 3.14.0 |
| SSH.NET | 2016.1.0 | 2024.2.0 |

## Phase 3: API Migrations

Fixed breaking API changes between .NET Framework and .NET 10:

- `DirectorySecurity` → `FileSystemAclExtensions`
- `NamedPipeServerStream` constructor (new overloads)
- `HttpUtility.UrlEncode` → `WebUtility.UrlEncode`
- `Assembly.GetExecutingAssembly().Location` → `Environment.ProcessPath`
  (critical — returns empty string in self-contained apps)

**Tests:** 578/578 unit tests pass. Functional test suite passes (429+ tests).

## Phase 4: Newtonsoft.Json → System.Text.Json

The largest single migration. Newtonsoft.Json uses reflection extensively,
making it incompatible with AOT trimming.

### New Infrastructure

- `GVFSJsonOptions.cs` — Shared `JsonSerializerOptions` with source-gen context chain
- `GVFSJsonContext.cs` — `[JsonSerializable]` attributes for ~20 types
- `VersionConverter.cs` — Handles `System.Version` as both string and object
- `EventMetadataConverter.cs` — AOT-safe `Dictionary<string, object>` serialization

### Key Lesson: STJ Source Generator Naming Collisions

The System.Text.Json source generator **silently drops types** when multiple
`[JsonSerializable]` attributes register nested types with the same short name.

Example: `GetStatus.Response`, `RegisterRepoRequest.Response`, `EnableAndAttachProjFSRequest.Response`
— all named "Response". Only one gets generated code.

**Fix:** Add `TypeInfoPropertyName` to disambiguate:
```csharp
[JsonSerializable(typeof(NamedPipeMessages.GetStatus.Response),
    TypeInfoPropertyName = "GetStatusResponse")]
[JsonSerializable(typeof(NamedPipeMessages.RegisterRepoRequest.Response),
    TypeInfoPropertyName = "RegisterRepoResponse")]
```

## Phase 5: Other Modernizations

### System.CommandLine
Replaced CommandLineParser 2.6.0 with System.CommandLine 2.0.3 (stable release).
15 verb commands with backward-compatible `version` subcommand.
The stable 2.0.x release uses `SetAction` with `ParseResult` instead of the beta's
`SetHandler` with `InvocationContext`.

### WMI → P/Invoke
Rewrote `WindowsPhysicalDiskInfo.cs` from WMI/COM to `DeviceIoControl` P/Invoke.
Eliminates COM interop dependency (blocked AOT trimmer).

## Phase 6: NativeAOT

**Branch:** `user/miniksa/net10-nativeaot`

### C++/CLI → Pure C# ProjFS

Created a pure C# P/Invoke replacement for `ProjectedFSLib.Managed.dll`:
- 5 source files, ~1,700 lines
- Same `Microsoft.Windows.ProjFS` namespace
- Struct layouts verified byte-by-byte against Windows SDK headers

**Critical bug found:** `PRJ_PLACEHOLDER_INFO` must include `VariableData[1]`
flexible array member. Without it, `Marshal.SizeOf` returns 336 instead of 344,
and `PrjWritePlaceholderInfo` returns `ERROR_INSUFFICIENT_BUFFER` for all directory
placeholder writes. Root enumerates fine (set up via `PrjMarkDirectoryAsVirtualizationRoot`),
but all subdirectories fail.

### PublishAot=true

All 5 managed executables compile to native binaries:

| Binary | Size | Description |
|--------|------|-------------|
| GVFS.exe | 14.5 MB | Main CLI |
| GVFS.Mount.exe | 13.2 MB | Mount daemon |
| GVFS.Service.exe | 5.9 MB | Windows service |
| GVFS.Service.UI.exe | 7.4 MB | Tray notification |
| GVFS.Hooks.exe | 2.8 MB | Git hooks |

### HTTP Performance: The NTLM Trap

Initial testing showed `SocketsHttpHandler` at ~400ms/request vs production's ~12ms/request.
Setting `Credentials = CredentialCache.DefaultCredentials` on the handler triggered unnecessary
NTLM authentication handshakes on every connection. The cache server accepts PAT/OAuth via
the `Authorization: Basic` header directly — NTLM adds no value and costs ~400ms per connection.

**Fix:** Use plain `SocketsHttpHandler` *without* setting `Credentials` or `ServerCredentials`.
Authentication is handled per-request via the `Authorization` header, matching production behavior.
Result: ~14ms/request — matching the .NET Framework production build.

### Build System Improvements

- **Centralized TargetFramework** — `Directory.Build.props` sets `net10.0-windows10.0.17763.0`
  for all C# projects. Only `GVFS.MSBuild.csproj` overrides to `netstandard2.0`.
- **Centralized packages** — `Directory.Packages.props` manages all NuGet package versions
  (`ManagePackageVersionsCentrally`). Individual csproj files use `<PackageReference>` without `Version`.
- **IjwHost removed** — `Directory.Build.targets` no longer copies `ijwhost.dll`. The C++/CLI
  `ProjectedFSLib.Managed.dll` is replaced by pure C# P/Invoke, making IjwHost unnecessary.

### Rejected: Managed Native Hooks

An attempt was made to rewrite the C++ native hooks (GitHooksLoader, ReadObjectHook,
PostIndexChangedHook, VirtualFileSystemHook) as a single managed NativeAOT executable
dispatching by `argv[0]`. While functional, the C++ hooks are small, fast, and well-tested.
The managed approach added unnecessary complexity without meaningful benefit. The C++ hooks
are retained.

## Benchmark Results

NativeAOT .NET 10 vs Production .NET Framework 4.7.1 (5 iterations each):

| Benchmark | .NET FW 4.7.1 | NativeAOT .NET 10 | Change |
|-----------|-------------:|-----------------:|--------|
| Startup (gvfs version) | 53.5 ± 2.6 ms | 15.3 ± 1.1 ms | **-71%** |
| Clone (small repo) | 5,234 ± 101 ms | 5,287 ± 127 ms | ~same |
| Mount (small repo) | 4,603 ± 82 ms | 4,148 ± 178 ms | **-10%** |
| Status (pipe roundtrip) | 341 ± 14 ms | 179 ± 10 ms | **-47%** |
| Git Status | 192 ± 16 ms | 139 ± 18 ms | **-27%** |
| Git Log (-100) | 140 ± 6 ms | 119 ± 3 ms | **-15%** |
| Dir Enumeration (ProjFS) | 187 ± 71 ms | 186 ± 56 ms | ~same |
| File Read (hydration) | 8.3 ± 13 ms | 1.8 ± 1.9 ms | **-78%** |
| Unmount | 874 ± 509 ms | 779 ± 621 ms | **-11%** |

**Zero regressions across all benchmarks.**

## Relationship to ProjFS-Managed-API

VFSForGit currently vendors the ProjFS P/Invoke code in `GVFS.Platform.Windows\ProjFS\`.
The same code has been contributed upstream to the ProjFS-Managed-API repository as a
pure C# replacement for the C++/CLI wrapper.

### Migration path:
1. ✅ ProjFS-Managed-API accepts the pure C# implementation
2. ✅ ProjFS-Managed-API publishes updated NuGet package
3. VFSForGit switches from vendored code to upstream NuGet reference
4. Both repos stay in sync; other ProjFS consumers benefit

## Conclusion

The migration from .NET Framework 4.7.1 to .NET 10 NativeAOT delivers:
- **3.5× faster startup** — native compilation eliminates JIT
- **2× faster IPC** — modern .NET pipe/JSON stack
- **78% faster file reads** — optimized ProjFS callback path
- **Simpler builds** — no C++ toolchain, no VC++ redistributable
- **Modern stack** — .NET 10, System.Text.Json, System.CommandLine
- **Zero regressions** — every benchmark equal or faster
