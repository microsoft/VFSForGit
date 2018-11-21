using System;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.RepairJobs
{
    public abstract class GitRefsRepairJob : RepairJob
    {
        protected GitRefsRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment) 
            : base(tracer, output, enlistment)
        {
        }

        public override IssueType HasIssue(List<string> messages)
        {
            int numBadRefs = 0;

            foreach (string gitRef in this.GetRefs())
            {
                if (!this.TryParseRef(gitRef, messages))
                {
                    numBadRefs++;
                }
            }

            return numBadRefs > 0 ? IssueType.Fixable : IssueType.None;
        }

        /// <summary>
        /// Fixes the refs using the reflog to find the last SHA.
        /// </summary>
        public override FixResult TryFixIssues(List<string> messages)
        {
            int numFailures = 0;

            foreach (string gitRef in this.GetRefs())
            {
                // We should only attempt to fix bad refs
                if (!this.TryParseRef(gitRef))
                {
                    if (!this.TryWriteRefFromLog(gitRef, messages))
                    {
                        numFailures++;
                    }
                }
            }

            if (numFailures > 0)
            {
                messages.Add($"Not all references could be fixed. Failed to fix {numFailures} references.");
                return FixResult.Failure;
            }

            return FixResult.Success;
        }

        /// <summary>
        /// Get the list of full symbolic references (eg. HEAD, refs/remotes/origin/HEAD, refs/heads/master)
        /// to inspect for and correct issues.
        /// </summary>
        /// <returns></returns>
        protected abstract IEnumerable<string> GetRefs();

        /// <summary>
        /// Check if the contents of a reference is valid.
        /// </summary>
        /// <param name="fullSymbolicRef">A full symbolic ref name. eg. HEAD, refs/remotes/origin/HEAD, refs/heads/master</param>
        /// <param name="refContents">Contents of the ref file</param>
        protected virtual bool IsValidRefContents(string fullSymbolicRef, string refContents)
        {
            // Check for symbolic references
            const string MinimallyValidHeadRef = "ref: refs/";
            if (refContents.StartsWith(MinimallyValidHeadRef, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Otherwise check for raw commit-style references
            return SHA1Util.IsValidShaFormat(refContents);
        }

        /// <summary>
        /// 'git ref-log' doesn't work if the repo is corrupted, so parsing reflogs seems like the only solution.
        /// </summary>
        /// <param name="fullSymbolicRef">A full symbolic ref name. eg. HEAD, refs/remotes/origin/HEAD, refs/heads/master</param>
        private static bool TryReadLastRefLogEntry(Enlistment enlistment, string fullSymbolicRef, out RefLogEntry refLog, out string error)
        {
            string refLogPath = Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Logs.Root, fullSymbolicRef);
            if (!File.Exists(refLogPath))
            {
                refLog = null;
                error = $"Could not find reflog for ref '{fullSymbolicRef}'";
                return false;
            }

            try
            {
                string refLogContents = File.ReadLines(refLogPath).Last();
                if (!RefLogEntry.TryParse(refLogContents, out refLog))
                {
                    error = $"Last ref log entry for '{fullSymbolicRef}' is unparsable.";
                    return false;
                }
            }
            catch (IOException ex)
            {
                refLog = null;
                error = $"IOException while reading reflog '{fullSymbolicRef}': " + ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        private bool TryParseRef(string fullSymbolicRef, List<string> messages = null)
        {
            string refPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Root, fullSymbolicRef);
            if (!File.Exists(refPath))
            {
                messages?.Add($"Could not find ref file for '{fullSymbolicRef}'");
                return false;
            }

            string refContents;
            try
            {
                refContents = File.ReadAllText(refPath).Trim();
            }
            catch (IOException ex)
            {
                messages?.Add($"IOException while reading '{refPath}': {ex.Message}");
                return false;
            }

            if (this.IsValidRefContents(fullSymbolicRef, refContents))
            {
                return true;
            }

            messages?.Add($"Invalid contents found in '{refPath}': {refContents}");
            return false;
        }

        private bool TryWriteRefFromLog(string fullSymbolicRef, List<string> messages)
        {
            string error;
            RefLogEntry refLog;
            if (!TryReadLastRefLogEntry(this.Enlistment, fullSymbolicRef, out refLog, out error))
            {
                this.Tracer.RelatedError(error);
                messages.Add(error);
                return false;
            }

            try
            {
                string refPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Root, fullSymbolicRef);
                File.WriteAllText(refPath, refLog.TargetSha + '\n');
                return true;
            }
            catch (IOException ex)
            {
                EventMetadata metadata = new EventMetadata();
                this.Tracer.RelatedError(metadata, $"Failed to write {fullSymbolicRef}: {ex}");
                return false;
            }
        }
    }
}
