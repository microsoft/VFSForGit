using CommandLine;
using GVFS.Common;
using GVFS.Common.Tracing;

namespace GVFS.CommandLine
{
    [Verb(LogVerb.LogVerbName, HelpText = "Show the most recent GVFS log")]
    public class LogVerb : GVFSVerb.ForExistingEnlistment
    {
        public const string LogVerbName = "log";

        protected override string VerbName
        {
            get { return LogVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment, ITracer tracer = null)
        {
            string logFile = enlistment.GetMostRecentGVFSLogFileName();
            if (logFile != null)
            {
                this.Output.WriteLine("Most recent log file: " + logFile);
            }
            else
            {
                this.Output.WriteLine("No log files found at " + enlistment.GVFSLogsRoot);
            }
        }
    }
}
