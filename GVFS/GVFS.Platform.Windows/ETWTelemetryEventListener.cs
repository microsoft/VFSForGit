using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;

using EventLevel = GVFS.Common.Tracing.EventLevel;
using EventOpcode = GVFS.Common.Tracing.EventOpcode;

namespace GVFS.Platform.Windows
{
    /// <summary>
    /// This class implements optional logging of ETW events, and it is disabled by default.
    /// See the CreateTelemetryListenerIfEnabled method for implementation details.
    /// 
    /// IF the user creates the gvfs.telemetry-id config setting, this class will:
    ///   * Listen for tracer events with the Telemetry keyword
    ///   * Record them as local ETW events
    ///   * Specify the value of the gvfs.telemetry-id config setting as a trait on the ETW events
    ///   
    /// In addition, if the gvfs.telemetry-id happens to contain a particular (and unpublished) GUID, 
    /// Windows will upload those ETW events to its telemetry stream as a background process. This is intended 
    /// for use only by teams internal to Microsoft who have access to the special GUID.
    /// 
    /// For any other teams who want to collect telemetry, what you will need to do is:
    ///   * Write any arbitrary value to the gvfs.telemetry-id config setting
    ///   * This will cause GVFS to write its telemetry events to local ETW, but nothing will get uploaded
    ///   * Write your own tool to scrape the local ETW events and analyze the data
    /// </summary>
    public class ETWTelemetryEventListener : InProcEventListener
    {
        private const long MeasureKeyword = 0x400000000000;

        private EventSource eventSource;

        private ETWTelemetryEventListener(string providerName, string[] traitsList) 
            : base(EventLevel.Verbose, Keywords.Telemetry)
        {           
            this.eventSource = new EventSource(providerName, EventSourceSettings.EtwSelfDescribingEventFormat, traitsList);
        }

        public static ETWTelemetryEventListener CreateTelemetryListenerIfEnabled(string gitBinRoot, string providerName)
        {
            // This listener is disabled unless the user specifies the proper git config setting.

            GitProcess.Result result = GitProcess.GetFromSystemConfig(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryId);
            if (result.HasErrors || string.IsNullOrEmpty(result.Output.TrimEnd('\r', '\n')))
            {
                result = GitProcess.GetFromGlobalConfig(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryId);
            }

            if (!result.HasErrors && !string.IsNullOrEmpty(result.Output.TrimEnd('\r', '\n')))
            {
                string[] traitsList = result.Output.TrimEnd('\r', '\n').Split('|');
                return new ETWTelemetryEventListener(providerName, traitsList);
            }
            else
            {
                return null;
            }
        }

        public override void Dispose()
        {
            if (this.eventSource != null)
            {
                this.eventSource.Dispose();
                this.eventSource = null;
            }
        }

        protected override void RecordMessageInternal(
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string jsonPayload)
        {
            EventSourceOptions options = this.CreateOptions(level, keywords, opcode);
            EventSource.SetCurrentThreadActivityId(activityId);

            if (jsonPayload != null)
            {
                JsonPayload payload = new JsonPayload(jsonPayload);
                this.eventSource.Write(eventName, ref options, ref activityId, ref parentActivityId, ref payload);
            }
            else
            {
                EmptyStruct payload = new EmptyStruct();
                this.eventSource.Write(eventName, ref options, ref activityId, ref parentActivityId, ref payload);
            }
        }

        private EventSourceOptions CreateOptions(EventLevel level, Keywords keywords, EventOpcode opcode)
        {
            EventSourceOptions options = new EventSourceOptions();
            options.Keywords = (EventKeywords)keywords;
            options.Keywords |= (EventKeywords)MeasureKeyword;

            options.Level = (Microsoft.Diagnostics.Tracing.EventLevel)level;
            options.Opcode = (Microsoft.Diagnostics.Tracing.EventOpcode)opcode;

            return options;
        }
        
        // Needed to pass relatedId without metadata
        [EventData]
        public struct EmptyStruct
        {
        }

        [EventData]
        public struct JsonPayload
        {
            public JsonPayload(string payload)
            {
                this.Json = payload;
            }

            [EventField]
            public string Json { get; }
        }
    }
}
