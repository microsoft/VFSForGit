using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.Common
{
    public class EnlistmentHydrationSummary
    {
        public int HydratedFileCount { get; private set; }
        public int TotalFileCount { get; private set; }
        public int HydratedFolderCount { get; private set; }
        public int TotalFolderCount { get; private set; }
        public Exception Error { get; private set; } = null;


        public bool IsValid
        {
            get
            {
                return HydratedFileCount >= 0
                && HydratedFolderCount >= 0
                && TotalFileCount >= HydratedFileCount
                && TotalFolderCount >= HydratedFolderCount;
            }
        }

        public string ToMessage()
        {
            if (!IsValid)
            {
                return "Error calculating hydration summary. Run 'gvfs health' at the repository root for hydration status details.";
            }

            int fileHydrationPercent = TotalFileCount == 0 ? 0 : (100 * HydratedFileCount) / TotalFileCount;
            int folderHydrationPercent = TotalFolderCount == 0 ? 0 : ((100 * HydratedFolderCount) / TotalFolderCount);
            return $"{fileHydrationPercent}% of files and {folderHydrationPercent}% of folders hydrated. Run 'gvfs health' at the repository root for details.";
        }

        public static EnlistmentHydrationSummary CreateSummary(
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            Func<int> projectedFolderCountProvider,
            CancellationToken cancellationToken = default)
        {
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            Stopwatch phaseStopwatch = new Stopwatch();

            try
            {
                /* Getting all the file paths from git index is slow and we only need the total count,
                 * so we read the index file header instead of calling GetPathsFromGitIndex */
                phaseStopwatch.Restart();
                int totalFileCount = GetIndexFileCount(enlistment, fileSystem);
                long indexReadMs = phaseStopwatch.ElapsedMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();

                EnlistmentPathData pathData = new EnlistmentPathData();

                /* FUTURE: These could be optimized to only deal with counts instead of full path lists */
                phaseStopwatch.Restart();
                pathData.LoadPlaceholdersFromDatabase(enlistment);
                long placeholderLoadMs = phaseStopwatch.ElapsedMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();

                phaseStopwatch.Restart();
                pathData.LoadModifiedPaths(enlistment, tracer);
                long modifiedPathsLoadMs = phaseStopwatch.ElapsedMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();

                int hydratedFileCount = pathData.ModifiedFilePaths.Count + pathData.PlaceholderFilePaths.Count;
                int hydratedFolderCount = pathData.ModifiedFolderPaths.Count + pathData.PlaceholderFolderPaths.Count;

                /* Getting the head tree count (used for TotalFolderCount) is potentially slower than the other parts
                 * of the operation, so we do it last and check that the other parts would succeed before running it.
                 */
                var soFar = new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = hydratedFileCount,
                    HydratedFolderCount = hydratedFolderCount,
                    TotalFileCount = totalFileCount,
                    TotalFolderCount = hydratedFolderCount + 1, // Not calculated yet, use a dummy valid value.
                };

                if (!soFar.IsValid)
                {
                    soFar.TotalFolderCount = 0; // Set to default invalid value to avoid confusion with the dummy value above.
                    tracer.RelatedWarning(
                        $"Hydration summary early exit: data invalid before tree count. " +
                        $"TotalFileCount={totalFileCount}, HydratedFileCount={hydratedFileCount}, " +
                        $"HydratedFolderCount={hydratedFolderCount}");
                    EmitDurationTelemetry(tracer, totalStopwatch.ElapsedMilliseconds, indexReadMs, placeholderLoadMs, modifiedPathsLoadMs, treeCountMs: 0, earlyExit: true);
                    return soFar;
                }

                /* Get the total folder count from the caller-provided function.
                 * In the mount process, this comes from the in-memory projection (essentially free).
                 * In gvfs health --status fallback, this parses the git index via GitIndexProjection. */
                cancellationToken.ThrowIfCancellationRequested();
                phaseStopwatch.Restart();
                int totalFolderCount = projectedFolderCountProvider();
                long treeCountMs = phaseStopwatch.ElapsedMilliseconds;

                EmitDurationTelemetry(tracer, totalStopwatch.ElapsedMilliseconds, indexReadMs, placeholderLoadMs, modifiedPathsLoadMs, treeCountMs, earlyExit: false);

                return new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = hydratedFileCount,
                    HydratedFolderCount = hydratedFolderCount,
                    TotalFileCount = totalFileCount,
                    TotalFolderCount = totalFolderCount,
                };
            }
            catch (OperationCanceledException)
            {
                return new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = -1,
                    HydratedFolderCount = -1,
                    TotalFileCount = -1,
                    TotalFolderCount = -1,
                };
            }
            catch (Exception e)
            {
                tracer.RelatedError($"Hydration summary failed with exception after {totalStopwatch.ElapsedMilliseconds}ms: {e.Message}");
                return new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = -1,
                    HydratedFolderCount = -1,
                    TotalFileCount = -1,
                    TotalFolderCount = -1,
                    Error = e,
                };
            }
        }

        private static void EmitDurationTelemetry(
            ITracer tracer,
            long totalMs,
            long indexReadMs,
            long placeholderLoadMs,
            long modifiedPathsLoadMs,
            long treeCountMs,
            bool earlyExit)
        {
            EventMetadata metadata = new EventMetadata();
            metadata["TotalMs"] = totalMs;
            metadata["IndexReadMs"] = indexReadMs;
            metadata["PlaceholderLoadMs"] = placeholderLoadMs;
            metadata["ModifiedPathsLoadMs"] = modifiedPathsLoadMs;
            metadata["TreeCountMs"] = treeCountMs;
            metadata["EarlyExit"] = earlyExit;
            tracer.RelatedEvent(
                EventLevel.Informational,
                "HydrationSummaryDuration",
                metadata,
                Keywords.Telemetry);
        }

        /// <summary>
        /// Get the total number of files in the index.
        /// </summary>
        internal static int GetIndexFileCount(GVFSEnlistment enlistment, PhysicalFileSystem fileSystem)
        {
            string indexPath = enlistment.GitIndexPath;
            using (var indexFile = fileSystem.OpenFileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, callFlushFileBuffers: false))
            {
                if (indexFile.Length < 12)
                {
                    return -1;
                }
                /* The number of files in the index is a big-endian integer from
                 * the 4 bytes at offsets 8-11 of the index file. */
                indexFile.Position = 8;
                var bytes = new byte[4];
                indexFile.Read(
                    bytes, // Destination buffer
                    offset: 0, // Offset in destination buffer, not in indexFile
                    count: 4);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                int count = BitConverter.ToInt32(bytes, 0);
                return count;
            }
        }

    }
}
