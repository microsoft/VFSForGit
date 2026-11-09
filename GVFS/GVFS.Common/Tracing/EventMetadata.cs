using System.Collections.Generic;

namespace GVFS.Common.Tracing
{
    // This is a convenience class to make code around event metadata look nicer.
    // It's more obvious to see EventMetadata than Dictionary<string, object> everywhere.
    public class EventMetadata : Dictionary<string, object>
    {
        public EventMetadata()
        {
        }

        public EventMetadata(Dictionary<string, object> metadata)
            : base(metadata)
        {
        }

        public TelemetryAggregation TelemetryAggregation { get; private set; }

        public void SetTelemetryAggregation(string operation, TelemetryFailureCategory failureCategory, string result, long count = 1, string previousState = null)
        {
            this.TelemetryAggregation = new TelemetryAggregation(operation, failureCategory, result, count, previousState);
            this["TelemetrySchemaVersion"] = GVFS.Common.Tracing.TelemetryAggregation.SchemaVersion;
            this["TelemetryAggregation"] = "Sample";
            this["Operation"] = operation;
            this["FailureCategory"] = failureCategory.ToString();
            this["AggregationResult"] = result;
        }
    }
}
