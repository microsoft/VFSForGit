using System;
using System.IO.Pipes;
using GVFS.Common.Git;
using Newtonsoft.Json;

namespace GVFS.Common.Tracing
{
    public class TelemetryDaemonEventListener : EventListener, IQueuedPipeStringWriterEventSink
    {
        private readonly string providerName;
        private readonly string enlistmentId;
        private readonly string mountId;
        private readonly string vfsVersion;

        private QueuedPipeStringWriter pipeWriter;

        private TelemetryDaemonEventListener(
            string providerName,
            string enlistmentId,
            string mountId,
            string pipeName,
            IEventListenerEventSink eventSink)
            : base(EventLevel.Verbose, Keywords.Telemetry, eventSink)
        {
            this.providerName = providerName;
            this.enlistmentId = enlistmentId;
            this.mountId = mountId;
            this.vfsVersion = ProcessHelper.GetCurrentProcessVersion();

            this.pipeWriter = new QueuedPipeStringWriter(
                () => new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous),
                this);
            this.pipeWriter.Start();
        }

        public string GitCommandSessionId { get; set; }

        public static TelemetryDaemonEventListener CreateIfEnabled(string gitBinRoot, string providerName, string enlistmentId, string mountId, IEventListenerEventSink eventSink)
        {
            // This listener is disabled unless the user specifies the proper git config setting.
            string telemetryPipe = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryPipe);
            if (!string.IsNullOrEmpty(telemetryPipe))
            {
                return new TelemetryDaemonEventListener(providerName, enlistmentId, mountId, telemetryPipe, eventSink);
            }
            else
            {
                return null;
            }
        }

        public override void Dispose()
        {
            if (this.pipeWriter != null)
            {
                this.pipeWriter.Stop();
                this.pipeWriter.Dispose();
                this.pipeWriter = null;
            }

            base.Dispose();
        }

        void IQueuedPipeStringWriterEventSink.OnStateChanged(
            QueuedPipeStringWriter writer,
            QueuedPipeStringWriterState state,
            Exception exception)
        {
            switch (state)
            {
                case QueuedPipeStringWriterState.Failing:
                    this.RaiseListenerFailure(exception?.ToString());
                    break;
                case QueuedPipeStringWriterState.Healthy:
                    this.RaiseListenerRecovery();
                    break;
            }
        }

        protected override void RecordMessageInternal(TraceEventMessage message)
        {
            string pipeMessage = this.CreatePipeMessage(message);

            bool dropped = !this.pipeWriter.TryEnqueue(pipeMessage);

            if (dropped)
            {
                this.RaiseListenerFailure("Pipe delivery queue is full. Message was dropped.");
            }
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

        private string CreatePipeMessage(TraceEventMessage message)
        {
            var pipeMessage = new PipeMessage
            {
                Version = this.vfsVersion,
                ProviderName = this.providerName,
                EventName = message.EventName,
                EventLevel = message.Level,
                EventOpcode = message.Opcode,
                Payload = new PipeMessage.PipeMessagePayload
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

        public class PipeMessage
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
            public PipeMessagePayload Payload { get; set; }

            public static PipeMessage FromJson(string json)
            {
                return JsonConvert.DeserializeObject<PipeMessage>(json);
            }

            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public class PipeMessagePayload
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
