using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    public class DehydratePruneBackupsVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string PruneBackupsVerbName = "prune-backups";

        // Backups can contain ProjFS placeholders that transiently fail to delete right after a
        // remount; retry with a short backoff so pruning reliably reclaims the space.
        private const int BackupDeleteRetryDelayMs = 1000;
        private const int BackupDeleteMaxRetries = 15;

        private PhysicalFileSystem fileSystem = new PhysicalFileSystem();

        public DehydratePruneBackupsVerb()
            : base(validateOrigin: false)
        {
        }

        /// <summary>
        /// When set, only this single backup folder is deleted (instead of all backups). Used by
        /// 'gvfs dehydrate --discard-backup', which launches this verb in a separate process to
        /// delete just the backup it created. Deleting from a fresh process avoids the ProjFS
        /// "provider temporarily unavailable" failure that occurs when the process that performed
        /// the dehydrate tries to delete the (now-orphaned) placeholders itself.
        /// </summary>
        public string SingleBackupPath { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command(
                PruneBackupsVerbName,
                "Delete backup folders left by previous 'gvfs dehydrate' runs. This does not perform a dehydrate.");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> backupPathOption = new System.CommandLine.Option<string>("--backup-path")
            {
                Description = "Delete only the specified backup folder instead of all backups.",
                Hidden = true,
            };
            cmd.Add(backupPathOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<DehydratePruneBackupsVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true,
                (verb, result) =>
                {
                    verb.SingleBackupPath = result.GetValue(backupPathOption);
                });

            return cmd;
        }

        protected override string VerbName
        {
            get { return PruneBackupsVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "PruneBackups"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Dehydrate),
                    EventLevel.Informational,
                    Keywords.Any);

                string backupParent = Path.GetFullPath(Path.Combine(enlistment.PrimaryEnlistmentRoot, DehydrateVerb.BackupFolderName));

                if (!string.IsNullOrEmpty(this.SingleBackupPath))
                {
                    this.PruneSingleBackup(tracer, backupParent);
                    return;
                }

                if (!this.fileSystem.DirectoryExists(backupParent))
                {
                    this.Output.WriteLine($"No backups to prune. No backup folder was found at {backupParent}.");
                    return;
                }

                string[] backups = Directory.GetDirectories(backupParent);
                if (backups.Length == 0)
                {
                    this.Output.WriteLine($"No backups to prune under {backupParent}.");
                    this.TryDeleteDirectory(tracer, backupParent);
                    return;
                }

                int deleted = 0;
                foreach (string backup in backups)
                {
                    if (this.TryDeleteDirectory(tracer, backup))
                    {
                        this.WriteMessage(tracer, $"Deleted backup folder {backup}.");
                        deleted++;
                    }
                    else
                    {
                        this.WriteMessage(tracer, $"WARNING: Failed to delete backup folder {backup}.");
                    }
                }

                this.TryRemoveEmptyParent(tracer, backupParent);

                this.WriteMessage(tracer, $"Pruned {deleted} of {backups.Length} backup folder(s).");

                if (deleted != backups.Length)
                {
                    this.ReportErrorAndExit(tracer, ReturnCode.GenericError, $"Failed to delete {backups.Length - deleted} backup folder(s).");
                }
            }
        }

        private void PruneSingleBackup(ITracer tracer, string backupParent)
        {
            string backup = Path.GetFullPath(this.SingleBackupPath);

            // Only ever delete something that is actually a backup folder for this enlistment.
            if (!this.IsPathWithin(backup, backupParent))
            {
                this.ReportErrorAndExit(tracer, ReturnCode.GenericError, $"Refusing to delete '{backup}': it is not a dehydrate backup folder under '{backupParent}'.");
            }

            if (!this.fileSystem.DirectoryExists(backup))
            {
                this.WriteMessage(tracer, $"Backup folder {backup} does not exist; nothing to delete.");
                this.TryRemoveEmptyParent(tracer, backupParent);
                return;
            }

            if (this.TryDeleteDirectory(tracer, backup))
            {
                this.WriteMessage(tracer, $"Deleted backup folder {backup}.");
                this.TryRemoveEmptyParent(tracer, backupParent);
            }
            else
            {
                this.ReportErrorAndExit(tracer, ReturnCode.GenericError, $"Failed to delete backup folder {backup}.");
            }
        }

        private bool IsPathWithin(string path, string parent)
        {
            string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, GVFSPlatform.Instance.Constants.PathComparison);
        }

        private void TryRemoveEmptyParent(ITracer tracer, string backupParent)
        {
            if (this.fileSystem.DirectoryExists(backupParent) &&
                !Directory.EnumerateFileSystemEntries(backupParent).Any())
            {
                this.TryDeleteDirectory(tracer, backupParent);
            }
        }

        private bool TryDeleteDirectory(ITracer tracer, string path)
        {
            // A backup can contain ProjFS placeholders. Delete them without recalling their
            // content through the (possibly busy or no-longer-projecting) provider, which would
            // otherwise fail with "provider ... temporarily unavailable".
            return this.fileSystem.TryDeleteDirectoryWithoutProviderRecall(tracer, path, BackupDeleteRetryDelayMs, BackupDeleteMaxRetries);
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            this.Output.WriteLine(message);
            tracer.RelatedEvent(
                EventLevel.Informational,
                "PruneBackups",
                new EventMetadata
                {
                    { TracingConstants.MessageKey.InfoMessage, message }
                });
        }
    }
}
