using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using RGFS.GVFlt.DotGit;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace RGFS.CommandLine.RepairJobs
{
    public abstract class RepairJob
    {
        private const string BackupExtension = ".bak";

        public RepairJob(ITracer tracer, TextWriter output, RGFSEnlistment enlistment)
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

        public enum FixResult
        {
            Success,
            Failure,
            ManualStepsRequired
        }

        public abstract string Name { get; }

        protected ITracer Tracer { get; }
        protected TextWriter Output { get; }
        protected RGFSEnlistment Enlistment { get; }

        public abstract IssueType HasIssue(List<string> messages);
        public abstract FixResult TryFixIssues(List<string> messages);

        protected bool TryRenameToBackupFile(string filePath, out string backupPath, List<string> messages)
        {
            backupPath = filePath + BackupExtension;
            try
            {
                File.Move(filePath, backupPath);
                this.Tracer.RelatedEvent(EventLevel.Informational, "FileMoved", new EventMetadata { { "SourcePath", filePath }, { "DestinationPath", backupPath } });
            }
            catch (Exception e)
            {
                messages.Add("Failed to back up " + filePath + " to " + backupPath);
                this.Tracer.RelatedError("Exception while moving " + filePath + " to " + backupPath + ": " + e.ToString());
                return false;
            }

            return true;
        }

        protected void RestoreFromBackupFile(string backupPath, string originalPath, List<string> messages)
        {
            try
            {
                File.Delete(originalPath);
                File.Move(backupPath, originalPath);
                this.Tracer.RelatedEvent(EventLevel.Informational, "FileMoved", new EventMetadata { { "SourcePath", backupPath }, { "DestinationPath", originalPath } });
            }
            catch (Exception e)
            {
                messages.Add("Could not restore " + originalPath + " from " + backupPath);
                this.Tracer.RelatedError("Exception while restoring " + originalPath + " from " + backupPath + ": " + e.ToString());
            }
        }

        protected bool TryDeleteFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
                this.Tracer.RelatedEvent(EventLevel.Informational, "FileDeleted", new EventMetadata { { "SourcePath", filePath } });
            }
            catch (Exception e)
            {
                this.Tracer.RelatedError("Exception while deleting file " + filePath + ": " + e.ToString());
                return false;
            }

            return true;
        }

        protected bool TryDeleteFolder(string filePath)
        {
            try
            {
                PhysicalFileSystem.RecursiveDelete(filePath);
                this.Tracer.RelatedEvent(EventLevel.Informational, "FolderDeleted", new EventMetadata { { "SourcePath", filePath } });
            }
            catch (Exception e)
            {
                this.Tracer.RelatedError("Exception while deleting folder " + filePath + ": " + e.ToString());
                return false;
            }

            return true;
        }

        protected IssueType TryParseIndex(string path, List<string> messages)
        {
            RGFSContext context = new RGFSContext(this.Tracer, null, null, this.Enlistment);

            using (GitIndexProjection index = new GitIndexProjection(
                context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: null,
                gvflt: null,
                placeholderList: null,
                sparseCheckout: null))
            {
                try
                {
                    index.BuildProjectionFromPath(path);
                }
                catch (Exception ex)
                {
                    messages.Add("Failed to parse index at " + path);
                    this.Tracer.RelatedInfo(ex.ToString());
                    return IssueType.Fixable;
                }
            }

            return IssueType.None;
        }
    }
}
