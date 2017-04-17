using CommandLine;
using GVFS.Common;

namespace GVFS.CommandLine
{
    [Verb(LogVerb.LogVerbName, HelpText = "Show the most recent GVFS log")]
    public class LogVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string LogVerbName = "log";

        protected override string VerbName
        {
            get { return LogVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            this.Output.WriteLine("Most recent log files:");
            this.DisplayMostRecent(enlistment, GVFSConstants.LogFileTypes.Clone);
            this.DisplayMostRecent(enlistment, GVFSConstants.LogFileTypes.Dehydrate);
            this.DisplayMostRecent(enlistment, GVFSConstants.LogFileTypes.Mount);
            this.DisplayMostRecent(enlistment, GVFSConstants.LogFileTypes.Prefetch);
        }

        private void DisplayMostRecent(GVFSEnlistment enlistment, string logFileType)
        {
            string logFile = enlistment.GetMostRecentGVFSLogFileName(logFileType);
            this.Output.WriteLine(
                "  {0}: {1}",
                logFileType,
                logFile == null ? "None" : logFile);
        }
    }
}
