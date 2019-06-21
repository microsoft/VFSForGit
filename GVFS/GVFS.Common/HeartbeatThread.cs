using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common
{
    public class HeartbeatThread
    {
        private static readonly TimeSpan HeartBeatWaitTime = TimeSpan.FromMinutes(60);

        private readonly ITracer tracer;
        private readonly IHeartBeatMetadataProvider dataProvider;

        private Timer timer;
        private DateTime startTime;
        private DateTime lastHeartBeatTime;

        public HeartbeatThread(ITracer tracer, IHeartBeatMetadataProvider dataProvider)
        {
            this.tracer = tracer;
            this.dataProvider = dataProvider;
        }

        public void Start()
        {
            this.startTime = DateTime.Now;
            this.lastHeartBeatTime = DateTime.Now;
            this.timer = new Timer(
                this.EmitHeartbeat,
                state: null,
                dueTime: HeartBeatWaitTime,
                period: HeartBeatWaitTime);
        }

        public void Stop()
        {
            using (WaitHandle waitHandle = new ManualResetEvent(false))
            {
                if (this.timer.Dispose(waitHandle))
                {
                    waitHandle.WaitOne();
                    waitHandle.Close();
                }
            }

            this.EmitHeartbeat(unusedState: null);
        }

        private void EmitHeartbeat(object unusedState)
        {
            try
            {
                EventMetadata metadata = this.dataProvider.GetAndResetHeartBeatMetadata(out bool writeToLogFile) ?? new EventMetadata();
                EventLevel eventLevel = writeToLogFile ? EventLevel.Informational : EventLevel.Verbose;
                DateTime now = DateTime.Now;
                metadata.Add("Version", ProcessHelper.GetCurrentProcessVersion());
                metadata.Add("MinutesUptime", (long)(now - this.startTime).TotalMinutes);
                metadata.Add("MinutesSinceLast", (int)(now - this.lastHeartBeatTime).TotalMinutes);
                this.lastHeartBeatTime = now;
                this.tracer.RelatedEvent(eventLevel, "Heartbeat", metadata, Keywords.Telemetry);
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "HeartbeatThread");
                metadata.Add("Exception", e.ToString());
                this.tracer.RelatedWarning(metadata, "Swallowing unhandled exception in EmitHeartbeat", Keywords.Telemetry);
            }
        }
    }
}
