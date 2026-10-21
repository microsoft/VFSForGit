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
        public int PlaceholderFileCount { get; private set; }
        public int PlaceholderFolderCount { get; private set; }
        public int ModifiedFileCount { get; private set; }
        public int ModifiedFolderCount { get; private set; }
        public int TotalFileCount { get; private set; }
        public int TotalFolderCount { get; private set; }
        public Exception Error { get; private set; } = null;
        public HydrationSummaryDiagnostics Diagnostics { get; private set; }

        public int HydratedFileCount => PlaceholderFileCount + ModifiedFileCount;
        public int HydratedFolderCount => PlaceholderFolderCount + ModifiedFolderCount;


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
            CancellationToken cancellationToken = default,
            HydrationSummaryDiagnostics diagnostics = null)
        {
            return CreateSummary(enlistment, fileSystem, tracer, projectedFolderCountProvider, new EnlistmentPathData(), cancellationToken, diagnostics);
        }

        internal static EnlistmentHydrationSummary CreateSummary(
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            Func<int> projectedFolderCountProvider,
            EnlistmentPathData pathData,
            CancellationToken cancellationToken = default,
            HydrationSummaryDiagnostics diagnostics = null)
        {
            diagnostics = diagnostics ?? new HydrationSummaryDiagnostics();
            EnlistmentHydrationSummary summary = new EnlistmentHydrationSummary { Diagnostics = diagnostics };
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            try
            {
                diagnostics.StartPhase(HydrationSummaryPhase.IndexRead);
                summary.TotalFileCount = GetIndexFileCount(enlistment, fileSystem, diagnostics);
                diagnostics.TotalFileCount = summary.TotalFileCount >= 0 ? summary.TotalFileCount : null;
                diagnostics.FinishPhase(summary.TotalFileCount >= 0 ? HydrationDataState.Complete : HydrationDataState.Failed);
                cancellationToken.ThrowIfCancellationRequested();

                diagnostics.StartPhase(HydrationSummaryPhase.Placeholders);
                pathData.LoadPlaceholdersFromDatabase(enlistment, diagnostics);
                summary.PlaceholderFileCount = pathData.PlaceholderFilePaths.Count;
                summary.PlaceholderFolderCount = pathData.PlaceholderFolderPaths.Count;
                diagnostics.PlaceholderFileCount = summary.PlaceholderFileCount;
                diagnostics.PlaceholderFolderCount = summary.PlaceholderFolderCount;
                diagnostics.FinishPhase(HydrationDataState.Complete);
                cancellationToken.ThrowIfCancellationRequested();

                diagnostics.StartPhase(HydrationSummaryPhase.ModifiedPaths);
                pathData.LoadModifiedPaths(enlistment, tracer, diagnostics);
                summary.ModifiedFileCount = pathData.ModifiedFilePaths.Count;
                summary.ModifiedFolderCount = pathData.ModifiedFolderPaths.Count;
                diagnostics.ModifiedFileCount = diagnostics.ModifiedPathsState == HydrationDataState.Failed ? null : summary.ModifiedFileCount;
                diagnostics.ModifiedFolderCount = diagnostics.ModifiedPathsState == HydrationDataState.Failed ? null : summary.ModifiedFolderCount;
                diagnostics.FinishPhase(diagnostics.ModifiedPathsState == HydrationDataState.NotStarted ? HydrationDataState.Complete : diagnostics.ModifiedPathsState);
                cancellationToken.ThrowIfCancellationRequested();

                // Preserve the early-exit check without reporting its temporary folder total as an observation.
                summary.TotalFolderCount = summary.HydratedFolderCount + 1;
                if (!summary.IsValid)
                {
                    summary.TotalFolderCount = 0;
                    diagnostics.EarlyExit = true;
                    diagnostics.Outcome = HydrationSummaryOutcome.InvalidCounts;
                    return summary;
                }

                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.StartPhase(HydrationSummaryPhase.FolderCount);
                summary.TotalFolderCount = projectedFolderCountProvider();
                diagnostics.TotalFolderCount = summary.TotalFolderCount;
                diagnostics.FinishPhase(HydrationDataState.Complete);
                diagnostics.Outcome = !summary.IsValid || (diagnostics.GetValidationFailures() & ~HydrationValidationFailure.MissingCounts) != HydrationValidationFailure.None ? HydrationSummaryOutcome.InvalidCounts
                    : diagnostics.CountsComplete ? HydrationSummaryOutcome.Success : HydrationSummaryOutcome.IncompleteData;
                return summary;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                diagnostics.Outcome = HydrationSummaryOutcome.Cancelled;
                diagnostics.FinishPhase(HydrationDataState.Cancelled);
                CapturePartialCounts(pathData, diagnostics);
                summary.InvalidateCounts();
                return summary;
            }
            catch (Exception e)
            {
                diagnostics.Outcome = HydrationSummaryOutcome.Failed;
                diagnostics.ExceptionType = e.GetType().FullName;
                diagnostics.ExceptionHResult = e.HResult;
                diagnostics.FinishPhase(HydrationDataState.Failed);
                CapturePartialCounts(pathData, diagnostics);

                tracer.RelatedWarning(new EventMetadata { ["Exception"] = e.ToString() }, "Hydration summary calculation failed.", Keywords.None);
                summary.Error = e;
                summary.InvalidateCounts();
                return summary;
            }
            finally
            {
                diagnostics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                diagnostics.IsValid = summary.IsValid;
                tracer.RelatedEvent(
                    diagnostics.Outcome == HydrationSummaryOutcome.Success || diagnostics.Outcome == HydrationSummaryOutcome.Cancelled
                        ? EventLevel.Informational : EventLevel.Warning,
                    "HydrationSummaryDuration",
                    diagnostics.GetMetadata(),
                    Keywords.Telemetry);
            }
        }

        private static void CapturePartialCounts(EnlistmentPathData pathData, HydrationSummaryDiagnostics diagnostics)
        {
            if (diagnostics.Phase == HydrationSummaryPhase.Placeholders
                && (pathData.PlaceholderFilePaths.Count > 0 || pathData.PlaceholderFolderPaths.Count > 0))
            {
                diagnostics.PlaceholderFileCount = pathData.PlaceholderFilePaths.Count;
                diagnostics.PlaceholderFolderCount = pathData.PlaceholderFolderPaths.Count;
                if (diagnostics.PlaceholderState == HydrationDataState.Failed)
                {
                    diagnostics.PlaceholderState = HydrationDataState.Partial;
                }
            }

            if (diagnostics.Phase == HydrationSummaryPhase.ModifiedPaths
                && (pathData.ModifiedFilePaths.Count > 0 || pathData.ModifiedFolderPaths.Count > 0))
            {
                diagnostics.ModifiedFileCount = pathData.ModifiedFilePaths.Count;
                diagnostics.ModifiedFolderCount = pathData.ModifiedFolderPaths.Count;
                if (diagnostics.ModifiedPathsState == HydrationDataState.Failed)
                {
                    diagnostics.ModifiedPathsState = HydrationDataState.Partial;
                }
            }
        }

        /// <summary>
        /// Get the total number of files in the index.
        /// </summary>
        internal static int GetIndexFileCount(GVFSEnlistment enlistment, PhysicalFileSystem fileSystem, HydrationSummaryDiagnostics diagnostics = null)
        {
            string indexPath = enlistment.GitIndexPath;
            using (var indexFile = fileSystem.OpenFileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, callFlushFileBuffers: false))
            {
                if (diagnostics != null)
                {
                    diagnostics.IndexLength = indexFile.Length;
                }

                if (indexFile.Length < 12)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.IndexReadReason = "ShortIndex";
                    }

                    return -1;
                }
                byte[] header = new byte[12];
                indexFile.ReadExactly(header, 0, header.Length);
                int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4));
                if (diagnostics != null)
                {
                    diagnostics.IndexVersion = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));
                    diagnostics.IndexSignatureValid = header[0] == 'D' && header[1] == 'I' && header[2] == 'R' && header[3] == 'C';
                    diagnostics.IndexReadReason = count < 0 ? "NegativeEntryCount" : "Success";
                }

                return count;
            }
        }

        private void InvalidateCounts()
        {
            this.PlaceholderFileCount = -1;
            this.PlaceholderFolderCount = -1;
            this.ModifiedFileCount = -1;
            this.ModifiedFolderCount = -1;
            this.TotalFileCount = -1;
            this.TotalFolderCount = -1;
        }
    }
}
