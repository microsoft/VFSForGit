using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.Common
{
    public class EnlistmentHydrationSummary
    {
        public int PlaceholderFileCount { get; private set; }
        public int PlaceholderFolderCount { get; private set; }
        public int ModifiedFileCount { get; private set; }
        public int ModifiedFolderCount { get; private set; }
        public int TotalFileCount { get; private set; }
        public int TotalFolderCount { get; private set; }
        public Exception Error { get; private set; } = null;

        public int HydratedFileCount => PlaceholderFileCount + ModifiedFileCount;
        public int HydratedFolderCount => PlaceholderFolderCount + ModifiedFolderCount;

        public int PlaceholderFilePercent => TotalFileCount <= 0 ? 0 : (int)((100L * PlaceholderFileCount) / TotalFileCount);
        public int ModifiedFilePercent => TotalFileCount <= 0 ? 0 : (int)((100L * ModifiedFileCount) / TotalFileCount);
        public int FileHydrationPercent => TotalFileCount <= 0 ? 0 : (int)((100L * HydratedFileCount) / TotalFileCount);
        public int FolderHydrationPercent => TotalFolderCount <= 0 ? 0 : (int)((100L * HydratedFolderCount) / TotalFolderCount);


        public bool IsValid
        {
            get
            {
                return PlaceholderFileCount >= 0
                && PlaceholderFolderCount >= 0
                && ModifiedFileCount >= 0
                && ModifiedFolderCount >= 0
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

            int fileHydrationPercent = TotalFileCount == 0 ? 0 : (int)((100L * HydratedFileCount) / TotalFileCount);
            int folderHydrationPercent = TotalFolderCount == 0 ? 0 : (int)((100L * HydratedFolderCount) / TotalFolderCount);
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

                int placeholderFileCount = pathData.PlaceholderFilePaths.Count;
                int placeholderFolderCount = pathData.PlaceholderFolderPaths.Count;
                int modifiedFileCount = pathData.ModifiedFilePaths.Count;
                int modifiedFolderCount = pathData.ModifiedFolderPaths.Count;

                /* Getting the head tree count (used for TotalFolderCount) is potentially slower than the other parts
                 * of the operation, so we do it last and check that the other parts would succeed before running it.
                 */
                var soFar = new EnlistmentHydrationSummary()
                {
                    PlaceholderFileCount = placeholderFileCount,
                    PlaceholderFolderCount = placeholderFolderCount,
                    ModifiedFileCount = modifiedFileCount,
                    ModifiedFolderCount = modifiedFolderCount,
                    TotalFileCount = totalFileCount,
                    TotalFolderCount = placeholderFolderCount + modifiedFolderCount + 1, // Not calculated yet, use a dummy valid value.
                };

                if (!soFar.IsValid)
                {
                    soFar.TotalFolderCount = 0; // Set to default invalid value to avoid confusion with the dummy value above.
                    tracer.RelatedWarning(
                        $"Hydration summary early exit: data invalid before tree count. " +
                        $"TotalFileCount={totalFileCount}, PlaceholderFileCount={placeholderFileCount}, " +
                        $"ModifiedFileCount={modifiedFileCount}, PlaceholderFolderCount={placeholderFolderCount}, " +
                        $"ModifiedFolderCount={modifiedFolderCount}");
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
                    PlaceholderFileCount = placeholderFileCount,
                    PlaceholderFolderCount = placeholderFolderCount,
                    ModifiedFileCount = modifiedFileCount,
                    ModifiedFolderCount = modifiedFolderCount,
                    TotalFileCount = totalFileCount,
                    TotalFolderCount = totalFolderCount,
                };
            }
            catch (OperationCanceledException)
            {
                tracer.RelatedInfo($"Hydration summary cancelled after {totalStopwatch.ElapsedMilliseconds}ms");
                return new EnlistmentHydrationSummary()
                {
                    PlaceholderFileCount = -1,
                    PlaceholderFolderCount = -1,
                    ModifiedFileCount = -1,
                    ModifiedFolderCount = -1,
                    TotalFileCount = -1,
                    TotalFolderCount = -1,
                };
            }
            catch (Exception e)
            {
                tracer.RelatedError($"Hydration summary failed with exception after {totalStopwatch.ElapsedMilliseconds}ms: {e.Message}");
                return new EnlistmentHydrationSummary()
                {
                    PlaceholderFileCount = -1,
                    PlaceholderFolderCount = -1,
                    ModifiedFileCount = -1,
                    ModifiedFolderCount = -1,
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
            using (Stream indexFile = fileSystem.OpenFileStream(
                enlistment.GitIndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, callFlushFileBuffers: false))
            {
                return GitIndexHelper.ReadEntryCount(indexFile);
            }
        }

    }
}
