using CommandLine;
using RGFS.Common;
using System.IO;
using System.Linq;

namespace RGFS.CommandLine
{
    [Verb(LogVerb.LogVerbName, HelpText = "Show the most recent RGFS log files")]
    public class LogVerb : RGFSVerb
    {
        private const string LogVerbName = "log";

        [Value(
            0,
            Required = false,
            Default = "",
            MetaName = "Enlistment Root Path",
            HelpText = "Full or relative path to the RGFS enlistment root")]
        public override string EnlistmentRootPath { get; set; }

        [Option(
            "type",
            Default = null,
            HelpText = "The type of log file to display on the console")]
        public string LogType { get; set; }

        protected override string VerbName
        {
            get { return LogVerbName; }
        }

        public override void Execute()
        {
            this.Output.WriteLine("Most recent log files:");

            string enlistmentRoot = Paths.GetRGFSEnlistmentRoot(this.EnlistmentRootPath);
            if (enlistmentRoot == null)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid RGFS enlistment",
                    this.EnlistmentRootPath);
            }

            string rgfsLogsRoot = Path.Combine(
                enlistmentRoot,
                RGFSConstants.DotRGFS.LogPath);

            if (this.LogType == null)
            {
                this.DisplayMostRecent(rgfsLogsRoot, RGFSConstants.LogFileTypes.Clone);

                // By using MountPrefix ("mount") DisplayMostRecent will display either mount_verb, mount_upgrade, or mount_process, whichever is more recent
                this.DisplayMostRecent(rgfsLogsRoot, RGFSConstants.LogFileTypes.MountPrefix);
                this.DisplayMostRecent(rgfsLogsRoot, RGFSConstants.LogFileTypes.Prefetch);
                this.DisplayMostRecent(rgfsLogsRoot, RGFSConstants.LogFileTypes.Dehydrate);
                this.DisplayMostRecent(rgfsLogsRoot, RGFSConstants.LogFileTypes.Repair);

                string serviceLogsRoot = Paths.GetServiceLogsPath(this.ServiceName);
                this.DisplayMostRecent(serviceLogsRoot, RGFSConstants.LogFileTypes.Service);
            }
            else
            {
                string logFile = FindNewestFileInFolder(rgfsLogsRoot, this.LogType);
                if (logFile == null)
                {
                    this.ReportErrorAndExit("No log file found");
                }
                else
                {
                    foreach (string line in File.ReadAllLines(logFile))
                    {
                        this.Output.WriteLine(line);
                    }
                }
            }
        }

        private static string FindNewestFileInFolder(string folderName, string logFileType)
        {
            string logFilePattern = GetLogFilePatternForType(logFileType);

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
            return "rgfs_" + logFileType + "_*.log";
        }

        private void DisplayMostRecent(string logFolder, string logFileType)
        {
            string logFile = FindNewestFileInFolder(logFolder, logFileType);
            this.Output.WriteLine(
                "  {0, -10}: {1}",
                logFileType,
                logFile == null ? "None" : logFile);
        }
    }
}
