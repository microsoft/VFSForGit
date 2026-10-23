using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    public class DehydratePruneBackupsVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string PruneBackupsVerbName = "prune-backups";

        private PhysicalFileSystem fileSystem = new PhysicalFileSystem();

        public DehydratePruneBackupsVerb()
            : base(validateOrigin: false)
        {
        }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command(
                PruneBackupsVerbName,
                "Delete backup folders left by previous 'gvfs dehydrate' runs. This does not perform a dehydrate. The repo must be unmounted, because the backups contain virtualization placeholders that can only be deleted while unmounted.");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<DehydratePruneBackupsVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true);

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

                if (!this.fileSystem.DirectoryExists(backupParent))
                {
                    this.Output.WriteLine($"No backups to prune. No backup folder was found at {backupParent}.");
                    return;
                }

                string[] backups = Directory.GetDirectories(backupParent);
                if (backups.Length == 0)
                {
                    this.Output.WriteLine($"No backups to prune under {backupParent}.");
                    this.TryDeleteDirectory(backupParent, out _);
                    return;
                }

                int deleted = 0;
                bool reportedMountedFailure = false;
                foreach (string backup in backups)
                {
                    if (this.TryDeleteDirectory(backup, out Exception exception))
                    {
                        this.WriteMessage(tracer, $"Deleted backup folder {backup}.");
                        deleted++;
                    }
                    else
                    {
                        this.WriteMessage(tracer, $"WARNING: Failed to delete backup folder {backup}: {exception?.Message}");

                        // A backup can contain ProjFS placeholders that can only be deleted while
                        // the repo is unmounted. If we're still mounted, that is the likely cause;
                        // tell the user rather than leaving them guessing.
                        if (!reportedMountedFailure && this.IsRepoMounted(enlistment))
                        {
                            reportedMountedFailure = true;
                            this.WriteMessage(tracer, "The repo is currently mounted. Backups contain virtualization placeholders that can only be deleted while the repo is unmounted. Run 'gvfs unmount', then re-run 'gvfs dehydrate prune-backups'.");
                        }
                    }
                }

                this.TryRemoveEmptyParent(backupParent);

                this.WriteMessage(tracer, $"Pruned {deleted} of {backups.Length} backup folder(s).");

                if (deleted != backups.Length)
                {
                    this.ReportErrorAndExit(tracer, ReturnCode.GenericError, $"Failed to delete {backups.Length - deleted} backup folder(s).");
                }
            }
        }

        private bool IsRepoMounted(GVFSEnlistment enlistment)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                return pipeClient.Connect();
            }
        }

        private void TryRemoveEmptyParent(string backupParent)
        {
            if (this.fileSystem.DirectoryExists(backupParent) &&
                !Directory.EnumerateFileSystemEntries(backupParent).Any())
            {
                this.TryDeleteDirectory(backupParent, out _);
            }
        }

        private bool TryDeleteDirectory(string path, out Exception exception)
        {
            return this.fileSystem.TryDeleteDirectory(path, out exception);
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
