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

        public void SetTelemetryAggregation(string operation, TelemetryFailureCategory failureCategory, string result, long count = 1)
        {
            this.TelemetryAggregation = new TelemetryAggregation(operation, failureCategory, result, count);
            this["TelemetrySchemaVersion"] = 1;
            this["TelemetryAggregation"] = "Sample";
            this["Operation"] = operation;
            this["FailureCategory"] = failureCategory.ToString();
            this["Result"] = result;
        }
    }
}
