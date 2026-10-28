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
        public TelemetryAggregation(string operation, TelemetryFailureCategory failureCategory, string result, long count = 1)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            this.Operation = operation;
            this.FailureCategory = failureCategory;
            this.Result = result;
            this.Count = count;
        }

        public string Operation { get; }
        public TelemetryFailureCategory FailureCategory { get; }
        public string Result { get; }
        public long Count { get; }
    }
}
