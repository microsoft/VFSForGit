using GVFS.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    /// <summary>
    /// Class to query the configured services on macOS
    /// </summary>
    public class MacDaemonController
    {
        private const string LaunchCtlPath = @"/bin/launchctl";
        private const string LaunchCtlArg = @"list";

        private IProcessRunner processRunner;

        public MacDaemonController(IProcessRunner processRunner)
        {
            this.processRunner = processRunner;
        }

        public bool TryGetDaemons(string currentUser, out List<DaemonInfo> daemons, out string error)
        {
            // Consider for future improvement:
            // Use Launchtl to run Launchctl as the "real" user, so we can get the process list from the user.
            ProcessResult result = this.processRunner.Run(LaunchCtlPath, "asuser " + currentUser + " "  + LaunchCtlPath + " " + LaunchCtlArg, true);

            if (result.ExitCode != 0)
            {
                error = result.Output;
                daemons = null;
                return false;
            }

            return this.TryParseOutput(result.Output, out daemons, out error);
        }

        private bool TryParseOutput(string output, out List<DaemonInfo> daemonInfos, out string error)
        {
            daemonInfos = new List<DaemonInfo>();

            // 1st line is the header, skip it
            foreach (string line in output.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                // The expected output is a list of tab delimited entried:
                // PID\tSTATUS\tLABEL
                string[] tokens = line.Split('\t');

                if (tokens.Length != 3)
                {
                    daemonInfos = null;
                    error = $"Unexpected number of tokens in line: {line}";
                    return false;
                }

                string label = tokens[2];
                bool isRunning = int.TryParse(tokens[0], out _);

                daemonInfos.Add(new DaemonInfo() { Name = label, IsRunning = isRunning });
            }

            error = null;
            return true;
        }

        public class DaemonInfo
        {
            public string Name { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}
