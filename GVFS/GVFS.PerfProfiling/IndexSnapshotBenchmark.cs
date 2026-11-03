using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Projection;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using static GVFS.Virtualization.Projection.GitIndexProjection;

namespace GVFS.PerfProfiling
{
    internal static class IndexSnapshotBenchmark
    {
        private const int RebuildRuns = 5;
        private const int WarmLookupPasses = 10;
        private const int WarmEnumerationPasses = 3;

        public static void Run(string indexPath, string queryPath)
        {
            indexPath = Path.GetFullPath(indexPath);
            string[] paths = JsonSerializer.Deserialize(
                File.ReadAllText(queryPath),
                SnapshotBenchmarkJsonContext.Default.StringArray);
            if (paths == null || paths.Length == 0 || paths.Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException("The query corpus must contain nonempty projected paths.", nameof(queryPath));
            }

            paths = paths.Select(path => path.Replace('/', Path.DirectorySeparatorChar)).ToArray();
            string[] lookups = paths
                .SelectMany((path, index) => new[] { path, path + ".projection-benchmark-missing-" + index })
                .ToArray();
            string[] folders = paths.Select(path => Path.GetDirectoryName(path) ?? string.Empty).Distinct().ToArray();
            string indexHash;
            using (FileStream stream = File.OpenRead(indexPath))
            {
                indexHash = Convert.ToHexString(SHA256.HashData(stream));
            }

            GVFSEnlistment enlistment = new GVFSEnlistment(
                Path.GetDirectoryName(indexPath),
                "https://example.invalid/projection-benchmark",
                GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(),
                authentication: null);
            using (GVFSContext context = new GVFSContext(NullTracer.Instance, new PhysicalFileSystem(), new SnapshotRepository(), enlistment))
            using (GitIndexProjection projection = new GitIndexProjection(
                context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: null,
                fileSystemVirtualizer: null,
                placeholderDatabase: null,
                sparseCollection: null,
                modifiedPaths: null))
            {
                GitIndexParser parser = new GitIndexParser(projection);
                MemoryReading before = ReadMemory();
                double firstBuildMs = Build(parser, indexPath);
                MemoryReading afterFirstBuild = ReadMemory();
                PoolReading pools = new PoolReading(
                    SortedFolderEntries.FilePoolSize(),
                    SortedFolderEntries.FolderPoolSize(),
                    LazyUTF8String.StringPoolSize(),
                    LazyUTF8String.StringPoolObjectsUsed(),
                    LazyUTF8String.BytePoolSize(),
                    LazyUTF8String.BytePoolBytesUsed());
                LatencyReading firstLookups = MeasureQueries(projection, lookups, enumerate: false, passes: 1);
                LatencyReading warmLookups = MeasureQueries(projection, lookups, enumerate: false, passes: WarmLookupPasses);
                LatencyReading firstEnumerations = MeasureQueries(projection, folders, enumerate: true, passes: 1);
                LatencyReading warmEnumerations = MeasureQueries(projection, folders, enumerate: true, passes: WarmEnumerationPasses);
                TreeReading firstTree = ReadTree(projection);
                double[] rebuildTimesMs = new double[RebuildRuns];
                for (int i = 0; i < rebuildTimesMs.Length; ++i)
                {
                    ReadMemory();
                    rebuildTimesMs[i] = Build(parser, indexPath);
                }

                MemoryReading afterRebuild = ReadMemory();
                TreeReading tree = ReadTree(projection);
                if (tree != firstTree)
                {
                    throw new InvalidDataException("The projection changed after a rebuild.");
                }

                MemoryReading afterAllNamesTouched = ReadMemory();
                BenchmarkReport report = new BenchmarkReport(
                    RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    new FileInfo(indexPath).Length,
                    indexHash,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(queryPath))),
                    firstBuildMs,
                    rebuildTimesMs,
                    before,
                    afterFirstBuild,
                    afterRebuild,
                    afterAllNamesTouched,
                    pools,
                    firstLookups,
                    warmLookups,
                    firstEnumerations,
                    warmEnumerations,
                    tree);
                Console.WriteLine(JsonSerializer.Serialize(report, SnapshotBenchmarkJsonContext.Default.BenchmarkReport));
                GC.KeepAlive(projection);
            }
        }

        private static double Build(GitIndexParser parser, string indexPath)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (FileStream stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read, GitIndexParser.PageSize))
            {
                parser.RebuildProjection(NullTracer.Instance, stream);
            }

            SortedFolderEntries.ShrinkPool();
            LazyUTF8String.ShrinkPool();
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static MemoryReading ReadMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true, compacting: true);
            long liveBytes = GC.GetTotalMemory(forceFullCollection: false);
            GCMemoryInfo heap = GC.GetGCMemoryInfo();
            using (Process process = Process.GetCurrentProcess())
            {
                return new MemoryReading(
                    liveBytes,
                    heap.TotalCommittedBytes,
                    heap.FragmentedBytes,
                    process.PrivateMemorySize64,
                    process.WorkingSet64,
                    process.PeakPagedMemorySize64);
            }
        }

        private static LatencyReading MeasureQueries(GitIndexProjection projection, string[] paths, bool enumerate, int passes)
        {
            long[] durations = new long[checked(paths.Length * passes)];
            int resultIndex = 0;
            long enumerationItems = 0;
            for (int pass = 0; pass < passes; ++pass)
            {
                for (int i = 0; i < paths.Length; ++i)
                {
                    long start = Stopwatch.GetTimestamp();
                    if (enumerate)
                    {
                        enumerationItems += projection.GetProjectedItems(CancellationToken.None, blobSizesConnection: null, paths[i]).Count;
                    }
                    else
                    {
                        bool found = projection.IsPathProjected(paths[i], out string _, out bool _);
                        if (found != (i % 2 == 0))
                        {
                            throw new InvalidDataException("A projection lookup did not match the query corpus.");
                        }
                    }

                    durations[resultIndex++] = Stopwatch.GetTimestamp() - start;
                }
            }

            Array.Sort(durations);
            double microsecondsPerTick = 1000000.0 / Stopwatch.Frequency;
            return new LatencyReading(
                durations.Length,
                enumerationItems,
                durations.Average() * microsecondsPerTick,
                Percentile(durations, 0.50) * microsecondsPerTick,
                Percentile(durations, 0.95) * microsecondsPerTick,
                Percentile(durations, 0.99) * microsecondsPerTick,
                durations[durations.Length - 1] * microsecondsPerTick);
        }

        private static long Percentile(long[] sorted, double percentile)
        {
            return sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
        }

        private static TreeReading ReadTree(GitIndexProjection projection)
        {
            int files = 0;
            int folders = 0;
            Stack<string> pending = new Stack<string>();
            pending.Push(string.Empty);
            byte[] metadata = new byte[29];
            byte[] separator = new byte[1];
            StringComparison comparison = GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                while (pending.Count > 0)
                {
                    string path = pending.Pop();
                    hash.AppendData(Encoding.UTF8.GetBytes(path));
                    hash.AppendData(separator);
                    List<ProjectedFileInfo> entries = projection.GetProjectedItems(CancellationToken.None, blobSizesConnection: null, path);
                    string previousName = null;
                    foreach (ProjectedFileInfo entry in entries)
                    {
                        if (previousName != null && string.Compare(previousName, entry.Name, comparison) >= 0)
                        {
                            throw new InvalidDataException("Projected directory entries are not strictly ordered.");
                        }

                        previousName = entry.Name;
                        hash.AppendData(Encoding.UTF8.GetBytes(entry.Name));
                        hash.AppendData(separator);
                        entry.Sha.ToBuffer(metadata);
                        BinaryPrimitives.WriteInt64LittleEndian(metadata.AsSpan(20, sizeof(long)), entry.Size);
                        metadata[28] = entry.IsFolder ? (byte)1 : (byte)0;
                        hash.AppendData(metadata);
                        if (entry.IsFolder)
                        {
                            ++folders;
                            pending.Push(Path.Combine(path, entry.Name));
                        }
                        else
                        {
                            ++files;
                        }
                    }
                }

                if (projection.GetProjectedFolderCount() != folders)
                {
                    throw new InvalidDataException("The projected folder counts do not match.");
                }

                return new TreeReading(files, folders, Convert.ToHexString(hash.GetHashAndReset()));
            }
        }

        private sealed class SnapshotRepository : GitRepo
        {
            public SnapshotRepository()
                : base(NullTracer.Instance)
            {
            }
        }
    }

    internal sealed record MemoryReading(
        long ManagedLiveBytes,
        long ManagedCommittedBytes,
        long ManagedFragmentedBytes,
        long PrivateBytes,
        long WorkingSetBytes,
        long PeakPrivateBytes);

    internal sealed record PoolReading(
        int FilesCapacity,
        int FoldersCapacity,
        int NamesCapacity,
        int NamesUsed,
        int BytesCapacity,
        int BytesUsed);

    internal sealed record LatencyReading(
        int Samples,
        long EnumerationItems,
        double MeanMicroseconds,
        double P50Microseconds,
        double P95Microseconds,
        double P99Microseconds,
        double MaxMicroseconds);

    internal sealed record TreeReading(int Files, int Folders, string Fingerprint);

    internal sealed record BenchmarkReport(
        string Runtime,
        string Framework,
        string Architecture,
        long IndexBytes,
        string IndexSha256,
        string QueriesSha256,
        double FirstBuildMs,
        double[] RebuildTimesMs,
        MemoryReading Before,
        MemoryReading AfterFirstBuild,
        MemoryReading AfterRebuild,
        MemoryReading AfterAllNamesTouched,
        PoolReading Pools,
        LatencyReading FirstLookups,
        LatencyReading WarmLookups,
        LatencyReading FirstEnumerations,
        LatencyReading WarmEnumerations,
        TreeReading Tree);

    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(BenchmarkReport))]
    internal partial class SnapshotBenchmarkJsonContext : JsonSerializerContext
    {
    }
}
