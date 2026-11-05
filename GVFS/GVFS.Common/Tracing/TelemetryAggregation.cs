using System;

namespace GVFS.Common.Tracing
{
    public enum TelemetryFailureCategory
    {
        CircuitBreakerOpen,
        NetworkUnavailable,
        SizesUnavailable,
        MissingObjectSize,
        None,
        EvictionUnavailable,
        BreakerState,
    }

    public sealed class TelemetryAggregation
    {
        internal const int SchemaVersion = 3;

        public TelemetryAggregation(string operation, TelemetryFailureCategory failureCategory, string result, long count = 1, string previousState = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            this.Operation = operation;
            this.FailureCategory = failureCategory;
            this.Result = result;
            this.Count = count;
            this.PreviousState = previousState;
        }

        public string Operation { get; }
        public TelemetryFailureCategory FailureCategory { get; }
        public string Result { get; }
        public long Count { get; }
        public string PreviousState { get; }
    }
}
