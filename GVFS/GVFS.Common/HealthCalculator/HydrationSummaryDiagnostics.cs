using GVFS.Common.Tracing;
using System;
using System.Diagnostics;

namespace GVFS.Common
{
    public enum HydrationSummaryOutcome
    {
        NotStarted,
        Success,
        InvalidCounts,
        IncompleteData,
        Cancelled,
        Failed,
    }

    public enum HydrationDataState
    {
        NotStarted,
        Complete,
        Partial,
        Failed,
        Cancelled,
    }

    public enum HydrationSummaryPhase
    {
        None,
        IndexRead,
        Placeholders,
        ModifiedPaths,
        FolderCount,
    }

    public enum HydrationFolderCountSource
    {
        Unknown,
        Projection,
        IndexParser,
    }

    public enum HydrationSummaryCaller
    {
        Unknown,
        BackgroundCache,
        HealthFallback,
    }

    public enum HydrationPathSourceOutcome
    {
        NotAttempted,
        Success,
        ConnectTimeout,
        ConnectFailed,
        MountNotReady,
        InvalidVersion,
        UnexpectedResponse,
        Failed,
    }

    [Flags]
    public enum HydrationValidationFailure
    {
        None = 0,
        MissingCounts = 1,
        NegativeCounts = 2,
        HydratedFilesExceedTotal = 4,
        HydratedFoldersExceedTotal = 8,
        FileSumOverflow = 16,
        FolderSumOverflow = 32,
    }

    public sealed class HydrationSummaryDiagnostics
    {
        private readonly Stopwatch phaseStopwatch = new Stopwatch();
        private bool phaseActive;

        public Guid CalculationId { get; } = Guid.NewGuid();
        public Guid? CommandId { get; set; }
        public HydrationSummaryCaller Caller { get; set; }
        public HydrationFolderCountSource FolderCountSource { get; set; }
        public HydrationSummaryOutcome Outcome { get; internal set; }
        public HydrationSummaryPhase Phase { get; private set; }
        public HydrationDataState IndexState { get; internal set; }
        public HydrationDataState PlaceholderState { get; internal set; }
        public HydrationDataState ModifiedPathsState { get; internal set; }
        public HydrationDataState FolderCountState { get; internal set; }
        public int? PlaceholderFileCount { get; internal set; }
        public int? PlaceholderFolderCount { get; internal set; }
        public int? ModifiedFileCount { get; internal set; }
        public int? ModifiedFolderCount { get; internal set; }
        public int? TotalFileCount { get; internal set; }
        public int? TotalFolderCount { get; internal set; }
        public int? PartialFolderCount { get; internal set; }
        public int? ExpandedFolderCount { get; internal set; }
        public int? PossibleTombstoneFolderCount { get; internal set; }
        public long? IndexLength { get; internal set; }
        public int? IndexVersion { get; internal set; }
        public bool? IndexSignatureValid { get; internal set; }
        public string IndexReadReason { get; internal set; }
        public HydrationPathSourceOutcome ModifiedPathsPipeOutcome { get; internal set; }
        public HydrationPathSourceOutcome ModifiedPathsDiskOutcome { get; internal set; }
        public long ModifiedPathsAddRecords { get; internal set; }
        public long ModifiedPathsDeleteRecords { get; internal set; }
        public long ModifiedPathsOtherRecords { get; internal set; }
        public string ModifiedPathsExceptionType { get; internal set; }
        public int? ModifiedPathsExceptionHResult { get; internal set; }
        public string ModifiedPathsPipeExceptionType { get; internal set; }
        public int? ModifiedPathsPipeExceptionHResult { get; internal set; }
        public long? IndexReadMs { get; private set; }
        public long? PlaceholderLoadMs { get; private set; }
        public long? ModifiedPathsLoadMs { get; private set; }
        public long? TreeCountMs { get; private set; }
        public long TotalMs { get; internal set; }
        public bool EarlyExit { get; internal set; }
        public bool? CancellationRequested { get; internal set; }
        public bool IsValid { get; internal set; }
        public string ExceptionType { get; internal set; }
        public int? ExceptionHResult { get; internal set; }

        public bool CountsComplete =>
            this.IndexState == HydrationDataState.Complete
            && this.PlaceholderState == HydrationDataState.Complete
            && this.ModifiedPathsState == HydrationDataState.Complete
            && this.FolderCountState == HydrationDataState.Complete;

        public HydrationValidationFailure GetValidationFailures()
        {
            HydrationValidationFailure failures = HydrationValidationFailure.None;
            if (!this.PlaceholderFileCount.HasValue || !this.PlaceholderFolderCount.HasValue
                || !this.ModifiedFileCount.HasValue || !this.ModifiedFolderCount.HasValue
                || !this.TotalFileCount.HasValue || !this.TotalFolderCount.HasValue)
            {
                failures |= HydrationValidationFailure.MissingCounts;
            }

            if (this.PlaceholderFileCount < 0 || this.PlaceholderFolderCount < 0
                || this.ModifiedFileCount < 0 || this.ModifiedFolderCount < 0
                || this.TotalFileCount < 0 || this.TotalFolderCount < 0)
            {
                failures |= HydrationValidationFailure.NegativeCounts;
            }

            long? files = (long?)this.PlaceholderFileCount + this.ModifiedFileCount;
            long? folders = (long?)this.PlaceholderFolderCount + this.ModifiedFolderCount;
            if (files > this.TotalFileCount)
            {
                failures |= HydrationValidationFailure.HydratedFilesExceedTotal;
            }

            if (folders > this.TotalFolderCount)
            {
                failures |= HydrationValidationFailure.HydratedFoldersExceedTotal;
            }

            if (files > int.MaxValue)
            {
                failures |= HydrationValidationFailure.FileSumOverflow;
            }

            if (folders > int.MaxValue)
            {
                failures |= HydrationValidationFailure.FolderSumOverflow;
            }

            return failures;
        }

        public EventMetadata GetMetadata()
        {
            return new EventMetadata
            {
                ["SchemaVersion"] = 1,
                [nameof(this.CalculationId)] = this.CalculationId.ToString("N"),
                [nameof(this.CommandId)] = this.CommandId?.ToString("N"),
                [nameof(this.Caller)] = this.Caller,
                [nameof(this.Outcome)] = this.Outcome,
                [nameof(this.Phase)] = this.Phase,
                [nameof(this.IsValid)] = this.IsValid,
                [nameof(this.CountsComplete)] = this.CountsComplete,
                ["ValidationFailures"] = this.GetValidationFailures(),
                [nameof(this.PlaceholderFileCount)] = this.PlaceholderFileCount,
                [nameof(this.PlaceholderFolderCount)] = this.PlaceholderFolderCount,
                [nameof(this.ModifiedFileCount)] = this.ModifiedFileCount,
                [nameof(this.ModifiedFolderCount)] = this.ModifiedFolderCount,
                [nameof(this.TotalFileCount)] = this.TotalFileCount,
                [nameof(this.TotalFolderCount)] = this.TotalFolderCount,
                ["HydratedFileCount"] = (long?)this.PlaceholderFileCount + this.ModifiedFileCount,
                ["HydratedFolderCount"] = (long?)this.PlaceholderFolderCount + this.ModifiedFolderCount,
                [nameof(this.IndexState)] = this.IndexState,
                [nameof(this.PlaceholderState)] = this.PlaceholderState,
                [nameof(this.ModifiedPathsState)] = this.ModifiedPathsState,
                [nameof(this.FolderCountState)] = this.FolderCountState,
                [nameof(this.FolderCountSource)] = this.FolderCountSource,
                [nameof(this.IndexLength)] = this.IndexLength,
                [nameof(this.IndexVersion)] = this.IndexVersion,
                [nameof(this.IndexSignatureValid)] = this.IndexSignatureValid,
                [nameof(this.IndexReadReason)] = this.IndexReadReason,
                [nameof(this.PartialFolderCount)] = this.PartialFolderCount,
                [nameof(this.ExpandedFolderCount)] = this.ExpandedFolderCount,
                [nameof(this.PossibleTombstoneFolderCount)] = this.PossibleTombstoneFolderCount,
                [nameof(this.ModifiedPathsPipeOutcome)] = this.ModifiedPathsPipeOutcome,
                [nameof(this.ModifiedPathsDiskOutcome)] = this.ModifiedPathsDiskOutcome,
                [nameof(this.ModifiedPathsAddRecords)] = this.ModifiedPathsAddRecords,
                [nameof(this.ModifiedPathsDeleteRecords)] = this.ModifiedPathsDeleteRecords,
                [nameof(this.ModifiedPathsOtherRecords)] = this.ModifiedPathsOtherRecords,
                [nameof(this.ModifiedPathsExceptionType)] = this.ModifiedPathsExceptionType,
                [nameof(this.ModifiedPathsExceptionHResult)] = this.ModifiedPathsExceptionHResult,
                [nameof(this.ModifiedPathsPipeExceptionType)] = this.ModifiedPathsPipeExceptionType,
                [nameof(this.ModifiedPathsPipeExceptionHResult)] = this.ModifiedPathsPipeExceptionHResult,
                [nameof(this.TotalMs)] = this.TotalMs,
                [nameof(this.IndexReadMs)] = this.IndexReadMs,
                [nameof(this.PlaceholderLoadMs)] = this.PlaceholderLoadMs,
                [nameof(this.ModifiedPathsLoadMs)] = this.ModifiedPathsLoadMs,
                [nameof(this.TreeCountMs)] = this.TreeCountMs,
                [nameof(this.EarlyExit)] = this.EarlyExit,
                [nameof(this.CancellationRequested)] = this.CancellationRequested,
                [nameof(this.ExceptionType)] = this.ExceptionType,
                [nameof(this.ExceptionHResult)] = this.ExceptionHResult,
            };
        }

        internal void StartPhase(HydrationSummaryPhase phase)
        {
            this.Phase = phase;
            this.phaseActive = true;
            this.phaseStopwatch.Restart();
        }

        internal void FinishPhase(HydrationDataState state)
        {
            if (!this.phaseActive)
            {
                return;
            }

            long elapsedMs = this.phaseStopwatch.ElapsedMilliseconds;
            switch (this.Phase)
            {
                case HydrationSummaryPhase.IndexRead:
                    this.IndexReadMs = elapsedMs;
                    this.IndexState = state;
                    break;
                case HydrationSummaryPhase.Placeholders:
                    this.PlaceholderLoadMs = elapsedMs;
                    this.PlaceholderState = state;
                    break;
                case HydrationSummaryPhase.ModifiedPaths:
                    this.ModifiedPathsLoadMs = elapsedMs;
                    this.ModifiedPathsState = state;
                    break;
                case HydrationSummaryPhase.FolderCount:
                    this.TreeCountMs = elapsedMs;
                    this.FolderCountState = state;
                    break;
            }

            this.phaseActive = false;
        }
    }
}
