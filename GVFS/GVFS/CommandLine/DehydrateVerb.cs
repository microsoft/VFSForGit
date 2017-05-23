using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using GVFS.GVFlt;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Text;

namespace GVFS.CommandLine
{
    [Verb(DehydrateVerb.DehydrateVerbName, HelpText = "EXPERIMENTAL FEATURE - Fully dehydrate a GVFS repo")]
    public class DehydrateVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string DehydrateVerbName = "dehydrate";

        [Option(
            "confirm",
            Default = false,
            Required = false,
            HelpText = "Pass in this flag to actually do the dehydrate")]
        public bool Confirmed { get; set; }

        [Option(
            "no-status",
            Default = false,
            Required = false,
            HelpText = "Skip 'git status' before dehydrating")]
        public bool NoStatus { get; set; }

        protected override string VerbName
        {
            get { return DehydrateVerb.DehydrateVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "Dehydrate"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Dehydrate),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    enlistment.CacheServerUrl,
                    new EventMetadata
                    {
                        { "Confirmed", this.Confirmed },
                        { "NoStatus", this.NoStatus },
                    });

                if (!this.Confirmed)
                {
                    this.Output.WriteLine(
@"WARNING: THIS IS AN EXPERIMENTAL FEATURE

Dehydrate will back up your src folder, and then create a new, empty src folder 
with a fresh virtualization of the repo. All of your downloaded objects, branches, 
and siblings of the src folder will be preserved. Your modified working directory 
files will be moved to the backup, and your new working directory will not have 
any of your uncommitted changes.

Before you dehydrate, make sure you have committed any working directory changes 
you want to keep. If you choose not to, you can still find your uncommitted changes 
in the backup folder, but it will be harder to find them because 'git status' 
will not work in the backup.

To actually execute the dehydrate, run 'gvfs dehydrate --confirm'
");

                    return;
                }

                this.CheckGitStatus(tracer, enlistment);

                string backupRoot = Path.GetFullPath(Path.Combine(enlistment.EnlistmentRoot, "dehydrate_backup", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                this.Output.WriteLine();
                this.WriteMessage(tracer, "Starting dehydration. All of your existing files will be backed up in " + backupRoot);
                this.WriteMessage(tracer, "WARNING: If you abort the dehydrate after this point, the repo may become corrupt");
                this.Output.WriteLine();

                this.Unmount(tracer);

                bool allowUpgrade = false;
                string error;
                if (!RepoMetadata.CheckDiskLayoutVersion(enlistment.DotGVFSRoot, allowUpgrade, out error))
                {
                    this.WriteErrorAndExit(tracer, "GVFS disk layout version doesn't match current version.  Run 'gvfs mount' first, then try dehydrate again.");
                }

                if (this.TryBackupFiles(tracer, enlistment, backupRoot) &&
                    this.TryRecreateIndex(tracer, enlistment))
                {
                    // Converting the src folder to partial must be the final step before mount
                    this.PrepareSrcFolder(tracer, enlistment);
                    this.Mount(tracer);

                    this.Output.WriteLine();
                    this.WriteMessage(tracer, "The repo was successfully dehydrated and remounted");
                }
                else
                {
                    this.Output.WriteLine();
                    this.WriteMessage(tracer, "ERROR: Backup failed. We will attempt to mount, but you may need to reclone if that fails");

                    this.Mount(tracer);
                    this.WriteMessage(tracer, "Dehydrate failed, but remounting succeeded");
                }
            }
        }

        private void CheckGitStatus(ITracer tracer, GVFSEnlistment enlistment)
        {
            if (!this.NoStatus)
            {
                this.WriteMessage(tracer, "Running git status before dehydrating to make sure you don't have any pending changes.");
                this.WriteMessage(tracer, "If this takes too long, you can abort and run dehydrate with --no-status to skip this safety check.");
                this.Output.WriteLine();

                bool isMounted = false;
                GitProcess.Result statusResult = null;
                if (!this.ShowStatusWhileRunning(
                    () =>
                    {
                        if (this.ExecuteGVFSVerb<StatusVerb>(tracer) != ReturnCode.Success)
                        {
                            return false;
                        }

                        isMounted = true;

                        GitProcess git = new GitProcess(enlistment);
                        statusResult = git.Status(allowObjectDownloads: false);
                        if (statusResult.HasErrors)
                        {
                            return false;
                        }

                        if (!statusResult.Output.Contains("nothing to commit, working tree clean"))
                        {
                            return false;
                        }

                        return true;
                    },
                    "Running git status",
                    suppressGvfsLogMessage: true))
                {
                    this.Output.WriteLine();

                    if (!isMounted)
                    {
                        this.WriteMessage(tracer, "Failed to run git status because the repo is not mounted");
                        this.WriteMessage(tracer, "Either mount first, or run with --no-status");
                    }
                    else if (statusResult.HasErrors)
                    {
                        this.WriteMessage(tracer, "Failed to run git status: " + statusResult.Errors);
                    }
                    else
                    {
                        this.WriteMessage(tracer, statusResult.Output);
                        this.WriteMessage(tracer, "git status reported that you have dirty files");
                        this.WriteMessage(tracer, "Either commit your changes or run dehydrate with --no-status");
                    }

                    this.WriteErrorAndExit(tracer, "Dehydrate was aborted");
                }
            }
        }

        private void Unmount(ITracer tracer)
        {
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    return
                        this.ExecuteGVFSVerb<StatusVerb>(tracer) != ReturnCode.Success ||
                        this.ExecuteGVFSVerb<UnmountVerb>(tracer) == ReturnCode.Success;
                },
                "Unmounting",
                suppressGvfsLogMessage: true))
            {
                this.WriteErrorAndExit(tracer, "Unable to unmount.");
            }
        }

        private void Mount(ITracer tracer)
        {
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    return this.ExecuteGVFSVerb<MountVerb>(tracer) == ReturnCode.Success;
                },
                "Mounting"))
            {
                this.WriteErrorAndExit(tracer, "Failed to mount after dehydrating.");
            }
        }

        private void PrepareSrcFolder(ITracer tracer, GVFSEnlistment enlistment)
        {
            string error;
            if (!GVFltCallbacks.TryPrepareFolderForGVFltCallbacks(enlistment.WorkingDirectoryRoot, out error))
            {
                this.WriteErrorAndExit(tracer, "Failed to recreate the virtualization root: " + error);
            }
        }

        private bool TryBackupFiles(ITracer tracer, GVFSEnlistment enlistment, string backupRoot)
        {
            string backupSrc = Path.Combine(backupRoot, "src");
            string backupGit = Path.Combine(backupRoot, ".git");
            string backupInfo = Path.Combine(backupGit, GVFSConstants.DotGit.Info.Name);
            string backupGvfs = Path.Combine(backupRoot, ".gvfs");

            string errorMessage = string.Empty;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    string ioError;
                    if (!this.TryIO(tracer, () => Directory.CreateDirectory(backupRoot), "Create backup directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupGit), "Create backup .git directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupInfo), "Create backup .git\\info directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupGvfs), "Create backup .gvfs directory", out ioError))
                    {
                        errorMessage = "Failed to create backup folders at " + backupRoot + ": " + ioError;
                        return false;
                    }

                    // Move the current src folder to the backup location...
                    if (!this.TryIO(tracer, () => Directory.Move(enlistment.WorkingDirectoryRoot, backupSrc), "Move the src folder", out ioError))
                    {
                        errorMessage = "Failed to move the src folder: " + ioError + Environment.NewLine;
                        errorMessage += "Make sure you have no open handles or running processes in the src folder";
                        return false;
                    }

                    // ... but move the .git folder back to the new src folder so we can preserve objects, refs, logs...
                    if (!this.TryIO(tracer, () => Directory.CreateDirectory(enlistment.WorkingDirectoryRoot), "Create new src folder", out errorMessage) ||
                        !this.TryIO(tracer, () => Directory.Move(Path.Combine(backupSrc, ".git"), enlistment.DotGitRoot), "Keep existing .git folder", out errorMessage))
                    {
                        return false;
                    }

                    // ... but then move the hydration-related files back to the backup...
                    if (!this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath),
                                Path.Combine(backupInfo, GVFSConstants.DotGit.Info.SparseCheckoutName)),
                            "Backup the sparse-checkout file",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () =>
                            {
                                if (File.Exists(Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.AlwaysExcludePath)))
                                {
                                    File.Move(
                                        Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.AlwaysExcludePath),
                                        Path.Combine(backupInfo, GVFSConstants.DotGit.Info.AlwaysExcludeName));
                                }
                            },
                            "Backup the always_exclude file",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... and recreate empty ones in the new .git folder...
                    if (!this.TryIO(
                            tracer,
                            () => File.AppendAllText(
                                Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath),
                                GVFSConstants.GitPathSeparatorString + GVFSConstants.SpecialGitFiles.GitAttributes + "\n"),
                            "Recreate a new sparse-checkout file",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup the .gvfs hydration-related data structures...
                    if (!this.TryIO(
                            tracer,
                            () => Directory.Move(Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BackgroundGitUpdates), Path.Combine(backupGvfs, GVFSConstants.DatabaseNames.BackgroundGitUpdates)),
                            "Backup the BackgroundGitUpdates database",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () => Directory.Move(Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.PlaceholderList), Path.Combine(backupGvfs, GVFSConstants.DatabaseNames.PlaceholderList)),
                            "Backup the PlaceholderList database",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup everything related to the .git\index...
                    if (!this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName),
                                Path.Combine(backupGit, GVFSConstants.DotGit.IndexName)),
                            "Backup the git index",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotGVFSRoot, "GVFS_projection"),
                                Path.Combine(backupGvfs, "GVFS_projection")),
                            "Backup GVFS_projection",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup all .git\*.lock files
                    foreach (string lockFile in Directory.GetFiles(enlistment.DotGitRoot, "*.lock"))
                    {
                        if (!this.TryIO(
                            tracer,
                            () => File.Move(
                                lockFile,
                                lockFile.Replace(enlistment.DotGitRoot, backupGit)),
                            "Backup " + lockFile.Replace(enlistment.DotGitRoot, string.Empty).Trim('\\'),
                            out errorMessage))
                        {
                            return false;
                        }
                    }

                    return true;
                },
                "Backing up your files"))
            {
                this.Output.WriteLine();
                this.WriteMessage(tracer, "ERROR: " + errorMessage);

                return false;
            }

            return true;
        }

        private bool TryRecreateIndex(ITracer tracer, GVFSEnlistment enlistment)
        {
            return this.ShowStatusWhileRunning(
                () =>
                {
                    // Create a new index based on the new minimal sparse-checkout
                    using (NamedPipeServer pipeServer = AllowAllLocksNamedPipeServer.Create(enlistment))
                    {
                        GitProcess git = new GitProcess(enlistment);
                        GitProcess.Result checkoutResult = git.ForceCheckout("HEAD");

                        return !checkoutResult.HasErrors;
                    }
                },
                "Recreating git index",
                suppressGvfsLogMessage: true);
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            this.Output.WriteLine(message);
            tracer.RelatedEvent(
                EventLevel.Informational,
                "Dehydrate",
                new EventMetadata
                {
                    { "Message", message }
                });
        }

        private void WriteErrorAndExit(ITracer tracer, string message)
        {
            tracer.RelatedError(message);
            this.ReportErrorAndExit("ERROR: " + message);
        }

        private ReturnCode ExecuteGVFSVerb<TVerb>(ITracer tracer)
            where TVerb : GVFSVerb, new()
        {
            try
            {
                ReturnCode returnCode;
                StringBuilder commandOutput = new StringBuilder();
                using (StringWriter writer = new StringWriter(commandOutput))
                {
                    returnCode = GVFSVerb.Execute<TVerb>(this.EnlistmentRootPath, verb => verb.Output = writer);
                }

                tracer.RelatedEvent(
                    EventLevel.Informational,
                    typeof(TVerb).Name,
                    new EventMetadata
                    {
                        { "Output", commandOutput.ToString() },
                        { "ReturnCode", returnCode }
                    });

                return returnCode;
            }
            catch (Exception e)
            {
                tracer.RelatedError(
                    new EventMetadata
                    {
                        { "Verb", typeof(TVerb).Name },
                        { "Exception", e.ToString() }
                    });

                return ReturnCode.GenericError;
            }
        }

        private bool TryIO(ITracer tracer, Action action, string description, out string error)
        {
            try
            {
                action();
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "TryIO",
                    new EventMetadata
                    {
                        { "Description", description }
                    });

                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                tracer.RelatedError(
                    new EventMetadata
                    {
                        { "Description", description },
                        { "Error", error },
                    });
            }

            return false;
        }
    }
}
