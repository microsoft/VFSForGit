using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Threading;

namespace GVFS.Common
{
    public class HeartbeatThread
    {
        private static readonly TimeSpan HeartBeatWaitTime = TimeSpan.FromMinutes(15);

        private readonly ITracer tracer;

        private Timer thread;
        private DateTime startTime;

        public HeartbeatThread(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public void Start()
        {
            this.startTime = DateTime.Now;
            this.thread = new Timer(
                this.EmitHeartbeat,
                state: null,
                dueTime: HeartBeatWaitTime,
                period: HeartBeatWaitTime);
        }

        private void EmitHeartbeat(object unusedState)
        {
            EventMetadata metadata = new Tracing.EventMetadata();
            metadata.Add("MinutesUptime", (long)(DateTime.Now - this.startTime).TotalMinutes);
            metadata.Add("MinutesSinceLast", (int)HeartBeatWaitTime.TotalMinutes);
            this.tracer.RelatedEvent(EventLevel.Verbose, "Heartbeat", metadata);
        }
    }
}
