# Index snapshot benchmark

The `--index-snapshot` mode measures a projection from an isolated copy of a version-4 Git index.
It does not mount a repository or open its databases.
The query corpus is a JSON array of projected file paths, relative to the repository root.
Paths can use Git separators or platform separators.

## Run the benchmark

Copy the index to a directory outside the live enlistment.
Keep the index and query corpus identical for both builds.
Do not use the live index as the benchmark input.

From the enlistment root, build the profiler:

```powershell
dotnet build src\GVFS\GVFS.PerfProfiling\GVFS.PerfProfiling.csproj `
    -c Release /p:PublishAot=false /p:VfsArch=arm64
```

Run the profiler:

```powershell
out\GVFS.PerfProfiling\bin\Release\net10.0-windows10.0.17763.0\win-arm64\GVFS.PerfProfiling.exe `
    --index-snapshot C:\bench\index C:\bench\queries.json > C:\bench\result.json
```

Use `VfsArch=x64` and `win-x64` for an x64 comparison.
For NativeAOT measurements, open a Visual Studio Developer PowerShell for the target architecture.

```powershell
dotnet publish src\GVFS\GVFS.PerfProfiling\GVFS.PerfProfiling.csproj `
    -c Release /p:VfsArch=arm64 /p:Platform=AnyCPU /p:IlcUseEnvironmentalTools=true
```

Run the executable from the corresponding `publish` directory.
Preserve each complete output directory before the next build.
Run each comparison in a separate process with the same architecture, runtime, and build configuration.

## Results and limits

The JSON report includes input hashes, runtime details, first-build time, and five rebuild times.
Build times include the pool shrink step.
Memory readings include forced-GC live bytes, managed commit, process private commit, and working set.
Pool readings show capacities and actual name and byte use after the first build.

Lookups alternate between existing paths and derived missing paths.
The benchmark checks each lookup result.
Enumeration queries use the distinct parent folders from the corpus.
Latency reports include p50, p95, and p99 values in microseconds.
First-pass results do not imply a cold OS file cache.

The benchmark walks the complete tree after the queries and after the final rebuild.
It checks directory order and compares fingerprints across those builds.
Fingerprints include paths, names, entry order, sizes, object IDs, and entry types.
The first walk converts all projected names to managed strings before the rebuild measurements.
The benchmark forces GC before each rebuild.
No size database is present, so file sizes remain at their initial projection values.

Forced GC affects only the benchmark process.
Private commit can remain high after objects become collectible.
The benchmark uses isolated projection pools, not a complete mount.
JIT measurements do not establish NativeAOT memory use or latency.
