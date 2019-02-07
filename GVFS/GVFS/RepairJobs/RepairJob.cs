using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Projection;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public abstract class RepairJob
    {
        private const string BackupExtension = ".bak";
        private PhysicalFileSystem fileSystem;

        public RepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
        {
            this.Tracer = tracer;
            this.Output = output;
            this.Enlistment = enlistment;
            this.fileSystem = new PhysicalFileSystem();
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
        protected GVFSEnlistment Enlistment { get; }

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
                this.fileSystem.RecursiveDelete(filePath);
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
            GVFSContext context = new GVFSContext(this.Tracer, null, null, this.Enlistment);

            using (GitIndexProjection index = new GitIndexProjection(
                context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: null,
                fileSystemVirtualizer: null,
                placeholderList: null,
                modifiedPaths: null))
            {
                try
                {
                    index.BuildProjectionFromPath(this.Tracer, path);
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
