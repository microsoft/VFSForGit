using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public abstract class RepairJob
    {
        public RepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
        {
            this.Tracer = tracer;
            this.Output = output;
            this.Enlistment = enlistment;
        }
        
        public enum IssueType
        {
            None,
            Fixable,
            CantFix
        }

        public abstract string Name { get; }

        protected ITracer Tracer { get; }
        protected TextWriter Output { get; }
        protected GVFSEnlistment Enlistment { get; }

        public abstract IssueType HasIssue(List<string> messages);
        public abstract bool TryFixIssues(List<string> messages);
    }
}
