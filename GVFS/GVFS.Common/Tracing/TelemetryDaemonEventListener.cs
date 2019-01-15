using System;
using System.IO.Pipes;
using System.Text;
using GVFS.Common.Git;
using Newtonsoft.Json;

namespace GVFS.Common.Tracing
{
    public class TelemetryDaemonEventListener : EventListener
    {
        private readonly string pipeName;
        private readonly string providerName;
        private readonly string enlistmentId;
        private readonly string mountId;
        private readonly string vfsVersion;

        private NamedPipeClientStream pipeClient;

        private TelemetryDaemonEventListener(
            string providerName,
            string enlistmentId,
            string mountId,
            string pipeName)
            : base(EventLevel.Verbose, Keywords.Telemetry)
        {
            this.pipeName = pipeName;
            this.providerName = providerName;
            this.enlistmentId = enlistmentId;
            this.mountId = mountId;
            this.vfsVersion = ProcessHelper.GetCurrentProcessVersion();
        }

        public static TelemetryDaemonEventListener CreateIfEnabled(string gitBinRoot, string providerName, string enlistmentId, string mountId, string pipeName)
        {
            // This listener is disabled unless the user specifies the proper git config setting.

            string telemetryId = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryId);
            if (!string.IsNullOrEmpty(telemetryId))
            {
                return new TelemetryDaemonEventListener(providerName, enlistmentId, mountId, pipeName);
            }
            else
            {
                return null;
            }
        }

        public override void Dispose()
        {
            this.pipeClient?.Dispose();
            base.Dispose();
        }

        protected override void RecordMessageInternal(
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string payload)
        {
            var message = new TelemetryDaemonMessage
            {
                Version = this.vfsVersion,
                ProviderName = this.providerName,
                EventName = eventName,
                EventLevel = level,
                EventOpcode = opcode,
                Payload = new TelemetryDaemonMessage.TelemetryDaemonMessagePayload
                {
                    EnlistmentId = this.enlistmentId,
                    MountId = this.mountId,
                    Json = payload
                },

                // TODO: do we need these?
                // ETW-only properties
                EtwActivityId = activityId,
                EtwParentActivityId = parentActivityId

                // Keywords are not used
            };

            string messageJson = message.ToJson();

            this.SendMessage(messageJson);
        }

        private static string GetConfigValue(string gitBinRoot, string configKey)
        {
            string value = string.Empty;
            string error;

            GitProcess.ConfigResult result = GitProcess.GetFromSystemConfig(gitBinRoot, configKey);
            if (!result.TryParseAsString(out value, out error, defaultValue: string.Empty) || string.IsNullOrWhiteSpace(value))
            {
                result = GitProcess.GetFromGlobalConfig(gitBinRoot, configKey);
                result.TryParseAsString(out value, out error, defaultValue: string.Empty);
            }

            return value.TrimEnd('\r', '\n');
        }

        private void SendMessage(string message)
        {
            // Create pipe if this is the first message, or if the last connection broke for any reason
            if (this.pipeClient == null)
            {
                var pipe = new NamedPipeClientStream(".", this.pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                try
                {
                    // Specify a instantaneous timeout because we don't want to hold up the rest of the
                    // application if the pipe is not available; we will just drop this event.
                    pipe.Connect(timeout: 0);
                    this.pipeClient = pipe;
                }
                catch (TimeoutException)
                {
                    // We can't connect; we will try again with a new pipe on the next message
                    return;
                }
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                this.pipeClient.Write(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                // We can't send this message for some reason (e.g., broken pipe); we attempt no recovery or retry
                // mechanism and drop this message. We will try to recreate/connect the pipe on the next message.
                this.pipeClient.Dispose();
                this.pipeClient = null;
            }
        }

        internal class TelemetryDaemonMessage
        {
            [JsonProperty("version")]
            public string Version { get; set; }
            [JsonProperty("providerName")]
            public string ProviderName { get; set; }
            [JsonProperty("eventName")]
            public string EventName { get; set; }
            [JsonProperty("eventLevel")]
            public EventLevel EventLevel { get; set; }
            [JsonProperty("eventOpcode")]
            public EventOpcode EventOpcode { get; set; }
            [JsonProperty("payload")]
            public TelemetryDaemonMessagePayload Payload { get; set; }
            [JsonProperty("etw.activityId")]
            public Guid EtwActivityId { get; set; }
            [JsonProperty("etw.parentActivityId")]
            public Guid EtwParentActivityId { get; set; }

            public static TelemetryDaemonMessage FromJson(string json)
            {
                return JsonConvert.DeserializeObject<TelemetryDaemonMessage>(json);
            }

            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public class TelemetryDaemonMessagePayload
            {
                [JsonProperty("enlistmentId")]
                public string EnlistmentId { get; set; }
                [JsonProperty("mountId")]
                public string MountId { get; set; }
                [JsonProperty("json")]
                public string Json { get; set; }
            }
        }
    }
}
