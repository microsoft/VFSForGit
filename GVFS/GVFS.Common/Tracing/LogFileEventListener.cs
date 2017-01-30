using Microsoft.Diagnostics.Tracing;
using System.IO;

namespace GVFS.Common.Tracing
{
    public class LogFileEventListener : InProcEventListener
    {
        private FileStream logFile;
        private TextWriter writer;

        public LogFileEventListener(string logFilePath, EventLevel maxVerbosity, Keywords keywordFilter)
            : base(maxVerbosity, keywordFilter)
        {
            this.logFile = File.Open(logFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            this.writer = StreamWriter.Synchronized(new StreamWriter(this.logFile));
        }

        public override void RecordMessage(string message)
        {
            this.writer.WriteLine(message);
            this.writer.Flush();
        }

        public override void Dispose()
        {
            if (this.writer != null)
            {
                this.writer.Dispose();
                this.writer = null;
            }

            if (this.logFile != null)
            {
                this.logFile.Dispose();
                this.logFile = null;
            }

            base.Dispose();
        }
    }
}
