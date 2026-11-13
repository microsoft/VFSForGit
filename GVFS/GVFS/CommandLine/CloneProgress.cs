using GVFS.Common.Tracing;
using System;
using System.Diagnostics;

namespace GVFS.CommandLine
{
    internal sealed class CloneProgress : IDisposable
    {
        private readonly ITracer tracer;
        private readonly Stopwatch timer = new Stopwatch();
        private volatile string message;
        private Stage? currentStage;
        private int attempt;

        public CloneProgress(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public enum Stage
        {
            ReadingRemoteReferences,
            PreparingRepositoryAndCache,
            PreparingCommitAndTrees,
            PreparingCheckout,
            InitializingIndex,
            FinalizingRepository,
        }

        public string GetMessage(int consoleWidth)
        {
            string currentMessage = this.message;
            int decorationWidth = "Cloning ()...|".Length + 1;
            if (currentMessage == null || consoleWidth < decorationWidth + 4)
            {
                return null;
            }

            int availableWidth = consoleWidth - decorationWidth;
            return currentMessage.Length <= availableWidth
                ? currentMessage
                : currentMessage.Substring(0, availableWidth - 3) + "...";
        }

        public void StartStage(Stage stage, int attempt = 1, bool retryPreviousStage = false)
        {
            this.FinishStage(retryPreviousStage ? "Retrying" : "Succeeded");
            this.currentStage = stage;
            this.attempt = attempt;
            string stageMessage = stage switch
            {
                Stage.ReadingRemoteReferences => "Reading remote references",
                Stage.PreparingRepositoryAndCache => "Preparing repository and cache",
                Stage.PreparingCommitAndTrees => "Preparing commit and trees",
                Stage.PreparingCheckout => "Preparing checkout",
                Stage.InitializingIndex => "Initializing index",
                Stage.FinalizingRepository => "Finalizing repository",
                _ => throw new ArgumentOutOfRangeException(nameof(stage)),
            };
            this.message = attempt > 1 ? stageMessage + " (retry)" : stageMessage;
            this.timer.Restart();
            this.TraceStage("Started");
        }

        public void Complete()
        {
            this.FinishStage("Succeeded");
        }

        public void Dispose()
        {
            this.FinishStage("Failed");
        }

        private void FinishStage(string state)
        {
            if (this.currentStage != null)
            {
                this.timer.Stop();
                this.TraceStage(state);
                this.currentStage = null;
                this.message = null;
            }
        }

        private void TraceStage(string state)
        {
            this.tracer.RelatedEvent(
                EventLevel.Informational,
                "CloneStage",
                new EventMetadata
                {
                    { "Stage", this.currentStage.Value.ToString() },
                    { "Attempt", this.attempt },
                    { "State", state },
                    { "DurationMs", this.timer.ElapsedMilliseconds },
                });
        }
    }
}
