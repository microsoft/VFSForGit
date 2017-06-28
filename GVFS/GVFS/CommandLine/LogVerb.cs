using CommandLine;
using GVFS.Common;
using GVFS.Service;
using System.IO;
using System.Linq;

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

            string gvfsLogsRoot = enlistment.GVFSLogsRoot;
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Clone), GVFSConstants.LogFileTypes.Clone);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Dehydrate), GVFSConstants.LogFileTypes.Dehydrate);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Mount), GVFSConstants.LogFileTypes.Mount);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Prefetch), GVFSConstants.LogFileTypes.Prefetch);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Repair), GVFSConstants.LogFileTypes.Repair);

            string serviceLogsRoot = GVFSService.GetServiceLogsRoot(this.ServiceName);
            this.DisplayMostRecent(serviceLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Service), GVFSConstants.LogFileTypes.Service);
        }

        private static string FindNewestFileInFolder(string folderName, string logFilePattern)
        {
            DirectoryInfo logDirectory = new DirectoryInfo(folderName);
            if (!logDirectory.Exists)
            {
                return null;
            }

            FileInfo[] files = logDirectory.GetFiles(logFilePattern ?? "*");
            if (files.Length == 0)
            {
                return null;
            }

            return
                files
                .OrderByDescending(fileInfo => fileInfo.CreationTime)
                .First()
                .FullName;
        }
        
        private static string GetLogFilePatternForType(string logFileType)
        {
            return "gvfs_" + logFileType + "_*.log";
        }

        private void DisplayMostRecent(string logFolder, string logFilePattern, string logDisplayName)
        {
            string logFile = FindNewestFileInFolder(logFolder, logFilePattern);
            this.Output.WriteLine(
                "  {0}: {1}",
                logDisplayName,
                logFile == null ? "None" : logFile);
        }
    }
}
