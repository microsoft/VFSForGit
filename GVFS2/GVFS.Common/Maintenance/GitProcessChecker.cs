using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GVFS.Common.Maintenance
{
    public class GitProcessChecker
    {
        public virtual IEnumerable<int> GetRunningGitProcessIds()
        {
            Process[] allProcesses = Process.GetProcesses();
            return allProcesses
                .Where(x => x.ProcessName.Equals("git", GVFSPlatform.Instance.Constants.PathComparison))
                .Select(x => x.Id);
        }
    }
}
