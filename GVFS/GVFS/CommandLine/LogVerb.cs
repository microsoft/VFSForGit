using CommandLine;
using GVFS.Common;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(LogVerb.LogVerbName, HelpText = "Show the most recent GVFS log files")]
    public class LogVerb : GVFSVerb
    {
        private const string LogVerbName = "log";

        [Value(
            0,
            Required = false,
            Default = "",
            MetaName = "Enlistment Root Path",
            HelpText = "Full or relative path to the GVFS enlistment root")]
        public override string EnlistmentRootPath { get; set; }

        protected override string VerbName
        {
            get { return LogVerbName; }
        }

        public override void Execute()
        {
            this.Output.WriteLine("Most recent log files:");

            string enlistmentRoot = Paths.GetGVFSEnlistmentRoot(this.EnlistmentRootPath);
            if (enlistmentRoot == null)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid GVFS enlistment",
                    this.EnlistmentRootPath);
            }

            string gvfsLogsRoot = Path.Combine(
                enlistmentRoot,
                GVFSConstants.DotGVFS.LogPath);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Clone), GVFSConstants.LogFileTypes.Clone);

            // By using MountPrefix ("mount") DisplayMostRecent will display either mount_verb, mount_upgrade, or mount_process, whichever is more recent
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.MountPrefix), GVFSConstants.LogFileTypes.MountPrefix);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Prefetch), GVFSConstants.LogFileTypes.Prefetch);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Dehydrate), GVFSConstants.LogFileTypes.Dehydrate);
            this.DisplayMostRecent(gvfsLogsRoot, GetLogFilePatternForType(GVFSConstants.LogFileTypes.Repair), GVFSConstants.LogFileTypes.Repair);

            string serviceLogsRoot = Paths.GetServiceLogsPath(this.ServiceName);
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
                "  {0, -10}: {1}",
                logDisplayName,
                logFile == null ? "None" : logFile);
        }
    }
}
