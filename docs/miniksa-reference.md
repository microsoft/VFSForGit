# Miniksa's NativeAOT Prototype — Applied vs. Remaining

**Reference branch:** [`miniksa:user/miniksa/net10-nativeaot`](https://github.com/miniksa/VFSForGit/tree/user/miniksa/net10-nativeaot)

Miniksa (Michael Niksa) did the original prototype work for migrating VFSForGit
to .NET 10 with NativeAOT. He also maintains the
[ProjFS-Managed-API](https://github.com/microsoft/ProjFS-Managed-API) package
which was rewritten from C++/CLI to pure C# P/Invoke for AOT compatibility
(v2.1.0, commit `0fff2cd`).

## What We Applied from Miniksa's Branch

### Fully Applied
- **TFM retarget approach:** `net10.0-windows10.0.17763.0` target framework
- **Directory.Build.props structure:** Centralized TFM, RID, AOT settings
- **PublishAot + SelfContained:** Same AOT compilation strategy
- **Test project opt-out:** `PublishAot=false` for test/tooling projects
- **Build.bat 3-step approach:** dotnet restore → VS MSBuild for C++ → dotnet publish for managed
- **Output path changes:** `publish\` subdirectory for managed AOT binaries
- **SDK pinning:** `global.json` with `rollForward: disable`
- **Microsoft.Windows.ProjFS 2.1.0:** Pure C# P/Invoke package replacing C++/CLI

### Applied with Modifications
- **GVFSJsonContext.cs:** We created our own source-generated JSON context with 25+ types
  (miniksa's branch had a similar but not identical set of serializable types — ours was built
  from scratch by analyzing all JSON serialization call sites)
- **GVFSJsonOptions.cs TypeInfoResolverChain:** Same concept (source-gen + reflection fallback)
  but our implementation includes IL2026/IL3050 suppressions and different ordering
- **layout.bat cleanup:** Same idea of removing managed artifacts, but our implementation
  also handles orphaned PDBs and runtimeconfig files
- **CreateBuildArtifacts.bat:** Updated for publish\ paths, but we also added trailing `\*`
  fixes for xcopy

### Not Yet Applied (Still in Miniksa's Branch)
- **Full WinHttpHandler replacement:** Miniksa replaced `HttpClientHandler` usage with
  `WinHttpHandler` for better AOT compatibility. We deferred this — current code uses
  `HttpClientHandler` with `UseDefaultCredentials` removed. May need this if HTTP-related
  AOT issues surface.
- **Additional trimming configuration:** Miniksa may have `TrimmerRootDescriptor.xml` or
  `ILLink` settings that we haven't ported.
- **ARM64-specific adjustments:** Miniksa's branch may have ARM64-specific changes beyond
  what we've done (we set `RuntimeIdentifier=win-x64` globally).

### Missed During Migration (Now Applied)
- **`UseShellExecute = true` in `StartBackgroundVFS4GProcess`:** Miniksa's branch had this
  fix. .NET Framework defaults `UseShellExecute` to `true` (ShellExecuteEx, no handle
  inheritance) while .NET Core/.NET 10 defaults to `false` (CreateProcess, handles
  inherited). Without this, the daemon `GVFS.Mount.exe` inherits the caller's
  stdout/stderr pipe handles, causing callers that read to EOF (e.g. git's hook runner
  via `ProcessHelper.Run`) to block indefinitely. This was the root cause of the slice 9
  functional test timeout.

## Work We Did That Wasn't in Miniksa's Branch

### Phase 1 & 2 (Merged Separately)
- **Dead code removal** (PRs #1937–#1939, #1941, #1946): Removed obsolete code paths,
  deprecated APIs, and unused dependencies to reduce the surface area before the TFM retarget
- **System.Text.Json migration:** Full migration from Newtonsoft.Json to STJ, including
  custom converters (EventMetadataConverter for `Dictionary<string, object>`)

### CI Infrastructure
- **functional-tests.yaml:** Added Verify GVFS installation diagnostic step, timeout handling,
  `--workers=1` sequential execution
- **RunFunctionalTests-Dev.ps1:** Created dev-mode functional test runner that works without
  admin/system install
- **CI debug logging:** `[CI-DEBUG]` output in test framework for diagnosing CI-specific issues

### Bug Fixes Discovered During Migration
- **Null triggeringProcessImageFileName guard:** ProjFS v2.1.0 regression where
  `Marshal.PtrToStringUni(IntPtr.Zero)` returns null instead of the old C++/CLI behavior
  of returning `String.Empty`. This affects any ProjFS consumer, not just VFSForGit.
- **CountingStream for DeflateStream truncation:** .NET 10 behavioral change where
  `DeflateStream` silently returns partial data on truncated zlib instead of throwing
  `InvalidDataException`. This is a general .NET migration concern, not VFSForGit-specific.
- **ConsoleOutputPayload visibility:** Made internal for source generator access
- **CentralPackageTransitivePinningEnabled:** Required for consistent dependency resolution
  with the CI NuGet feed

### Payload/Installer Cleanup
- **Removed all ProjectReferences from GVFS.Payload.csproj:** Build.bat handles ordering;
  ProjectReferences caused issues with mixed C++/managed builds
- **Removed CopyToOutputDirectory for hooks:** GVFS.Hooks binaries are placed by Build.bat,
  not by project references
- **FilesToSign updates:** Only native executables in the AOT payload need signing

## Key Differences from Miniksa's Approach

1. **We split into phases (1–5)** while miniksa's branch was a single large change.
   This makes the migration reviewable and bisectable.

2. **We kept `DefaultJsonTypeInfoResolver` as fallback** in the TypeInfoResolverChain.
   Miniksa may have been more aggressive about removing reflection-based JSON. Our approach
   is more conservative — source-gen for known types, reflection for unknown — which avoids
   runtime failures from missing type metadata at the cost of larger binaries.

3. **We use `dotnet publish` instead of `dotnet build`** for managed projects. `dotnet build`
   with PublishAot produces apphost stubs without Win32 version resources; `dotnet publish`
   produces proper native binaries that InnoSetup's `GetFileVersion` can read.

4. **SDK version pinning:** We pin to `10.0.202` with `rollForward: disable` to prevent
   CI from using a different SDK version that would pull different runtime pack versions
   through the NuGet feed.
