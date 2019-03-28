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

        public string GitCommandSessionId { get; set; }

        public static TelemetryDaemonEventListener CreateIfEnabled(string gitBinRoot, string providerName, string enlistmentId, string mountId)
        {
            // This listener is disabled unless the user specifies the proper git config setting.

            string telemetryPipe = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryPipe);
            if (!string.IsNullOrEmpty(telemetryPipe))
            {
                return new TelemetryDaemonEventListener(providerName, enlistmentId, mountId, telemetryPipe);
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

        protected override void RecordMessageInternal(TraceEventMessage message)
        {
            string json = this.SerializeMessage(message);
            this.SendMessage(json);
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

        private string SerializeMessage(TraceEventMessage message)
        {
            var pipeMessage = new TelemetryMessage
            {
                Version = this.vfsVersion,
                ProviderName = this.providerName,
                EventName = message.EventName,
                EventLevel = message.Level,
                EventOpcode = message.Opcode,
                Payload = new TelemetryMessage.TelemetryMessagePayload
                {
                    EnlistmentId = this.enlistmentId,
                    MountId = this.mountId,
                    GitCommandSessionId = this.GitCommandSessionId,
                    Json = message.Payload
                },

                // Other TraceEventMessage properties are not used
            };

            return pipeMessage.ToJson();
        }

        private void SendMessage(string message)
        {
            // Create pipe if this is the first message, or if the last connection broke for any reason
            if (this.pipeClient == null)
            {
                var pipe = new NamedPipeClientStream(".", this.pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

                // Specify a instantaneous timeout because we don't want to hold up the rest of the
                // application if the pipe is not available; we will just drop this event.
                // We let any TimeoutExceptions bubble up and will try again with a new pipe on the next SendMessage call.
                pipe.Connect(timeout: 0);

                // Keep a hold of this connected pipe for future messages
                this.pipeClient = pipe;
            }

            try
            {
                // If we're in byte/stream transmission mode rather than message mode
                // we should signal the end of each message with a line-feed (LF) character.
                if (this.pipeClient.TransmissionMode == PipeTransmissionMode.Byte)
                {
                    message += '\n';
                }

                var buffer = Encoding.UTF8.GetBytes(message);
                this.pipeClient.Write(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                // We can't send this message for some reason (e.g., broken pipe); we attempt no recovery or retry
                // mechanism and drop this message. We will try to recreate/connect the pipe on the next message.
                this.pipeClient.Dispose();
                this.pipeClient = null;
                throw;
            }
        }

        public class TelemetryMessage
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
            public TelemetryMessagePayload Payload { get; set; }

            public static TelemetryMessage FromJson(string json)
            {
                return JsonConvert.DeserializeObject<TelemetryMessage>(json);
            }

            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public class TelemetryMessagePayload
            {
                [JsonProperty("enlistmentId")]
                public string EnlistmentId { get; set; }
                [JsonProperty("mountId")]
                public string MountId { get; set; }
                [JsonProperty("gitCommandSessionId")]
                public string GitCommandSessionId { get; set; }
                [JsonProperty("json")]
                public string Json { get; set; }
            }
        }
    }
}
