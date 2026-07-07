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

        private PhysicalFileSystem fileSystem = new PhysicalFileSystem();

        public DehydratePruneBackupsVerb()
            : base(validateOrigin: false)
        {
        }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command(
                PruneBackupsVerbName,
                "Delete backup folders left by previous 'gvfs dehydrate' runs. This does not perform a dehydrate.");

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

                string backupParent = Path.Combine(enlistment.PrimaryEnlistmentRoot, DehydrateVerb.BackupFolderName);

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

                // Remove the now-empty parent folder if everything was pruned.
                if (!Directory.EnumerateFileSystemEntries(backupParent).Any())
                {
                    this.TryDeleteDirectory(tracer, backupParent);
                }

                this.WriteMessage(tracer, $"Pruned {deleted} of {backups.Length} backup folder(s).");
            }
        }

        private bool TryDeleteDirectory(ITracer tracer, string path)
        {
            try
            {
                this.fileSystem.DeleteDirectory(path);
                return true;
            }
            catch (Exception e)
            {
                tracer.RelatedError($"Failed to delete '{path}': {e.Message}");
                return false;
            }
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
