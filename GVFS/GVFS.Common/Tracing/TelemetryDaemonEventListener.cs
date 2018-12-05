using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using GVFS.Common.Git;
using Newtonsoft.Json;

namespace GVFS.Common.Tracing
{
    public class TelemetryDaemonEventListener : EventListener
    {
        private readonly string providerName;
        private readonly string enlistmentId;
        private readonly string mountId;
        private readonly string vfsVersion;

        private NamedPipeClientStream pipeClient;

        private TelemetryDaemonEventListener(
            string providerName,
            string enlistmentId,
            string mountId)
            : base(EventLevel.Verbose, Keywords.Telemetry)
        {
            this.providerName = providerName;
            this.enlistmentId = enlistmentId;
            this.mountId = mountId;
            this.vfsVersion = ProcessHelper.GetCurrentProcessVersion();
        }

        public static TelemetryDaemonEventListener CreateIfEnabled(string gitBinRoot, string providerName, string enlistmentId, string mountId)
        {
            // This listener is disabled unless the user specifies the proper git config setting.

            string telemetryId = GetConfigValue(gitBinRoot, GVFSConstants.GitConfig.GVFSTelemetryId);
            if (!string.IsNullOrEmpty(telemetryId))
            {
                return new TelemetryDaemonEventListener(providerName, enlistmentId, mountId);
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

        internal static string CreateJsonMessage(
            string vfsVersion,
            string providerName,
            string enlistmentId,
            string mountId,
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string payload)
        {
            var message = new Dictionary<string, object>
            {
                ["version"] = vfsVersion,
                ["providerName"] = providerName,
                ["eventName"] = eventName,
                ["eventLevel"] = ((int)level).ToString(),
                ["eventOpcode"] = ((int)opcode).ToString(),
                ["payload"] = new Dictionary<string, string>
                {
                    ["enlistmentId"] = enlistmentId,
                    ["mountId"] = mountId,
                    ["json"] = payload,
                },

                // TODO: do we need these?
                // ETW-only properties
                ["etw.activityId"] = activityId.ToString("D"),
                ["etw.parentActivityId"] = parentActivityId.ToString("D"),
            };

            return JsonConvert.SerializeObject(message);
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
            string message = CreateJsonMessage(
                this.vfsVersion,
                this.providerName,
                this.enlistmentId,
                this.mountId,
                eventName,
                activityId,
                parentActivityId,
                level,
                keywords,
                opcode,
                payload);

            this.SendMessage(message);
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
                string pipeName = GVFSPlatform.Instance.GetTelemetryNamedPipeName();
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
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
    }
}
