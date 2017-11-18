using CommandLine;
using RGFS.CommandLine.DiskLayoutUpgrades;
using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.NamedPipes;
using RGFS.Common.Tracing;
using RGFS.GVFlt;
using RGFS.GVFlt.DotGit;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Text;

namespace RGFS.CommandLine
{
    [Verb(DehydrateVerb.DehydrateVerbName, HelpText = "EXPERIMENTAL FEATURE - Fully dehydrate a RGFS repo")]
    public class DehydrateVerb : RGFSVerb.ForExistingEnlistment
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

        protected override void Execute(RGFSEnlistment enlistment)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer(RGFSConstants.RGFSEtwProviderName, "Dehydrate"))
            {
                tracer.AddLogFileEventListener(
                    RGFSEnlistment.GetNewRGFSLogFileName(enlistment.RGFSLogsRoot, RGFSConstants.LogFileTypes.Dehydrate),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    CacheServerResolver.GetUrlFromConfig(enlistment),
                    enlistment.GitObjectsRoot,
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

To actually execute the dehydrate, run 'rgfs dehydrate --confirm'
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
                
                string error;
                if (!DiskLayoutUpgrade.TryCheckDiskLayoutVersion(tracer, enlistment.EnlistmentRoot, out error))
                {
                    this.WriteErrorAndExit(tracer, error);
                }

                if (this.TryBackupFiles(tracer, enlistment, backupRoot))
                {
                    if (this.TryDownloadGitObjects(tracer, enlistment) &&
                        this.TryRecreateIndex(tracer, enlistment))
                    {
                        // Converting the src folder to partial must be the final step before mount
                        this.PrepareSrcFolder(tracer, enlistment);
                        this.Mount(tracer);

                        this.Output.WriteLine();
                        this.WriteMessage(tracer, "The repo was successfully dehydrated and remounted");
                    }
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

        private void CheckGitStatus(ITracer tracer, RGFSEnlistment enlistment)
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
                        if (this.ExecuteRGFSVerb<StatusVerb>(tracer) != ReturnCode.Success)
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
                    suppressRgfsLogMessage: true))
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
                        this.ExecuteRGFSVerb<StatusVerb>(tracer) != ReturnCode.Success ||
                        this.ExecuteRGFSVerb<UnmountVerb>(tracer) == ReturnCode.Success;
                },
                "Unmounting",
                suppressRgfsLogMessage: true))
            {
                this.WriteErrorAndExit(tracer, "Unable to unmount.");
            }
        }

        private void Mount(ITracer tracer)
        {
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    return this.ExecuteRGFSVerb<MountVerb>(tracer) == ReturnCode.Success;
                },
                "Mounting"))
            {
                this.WriteErrorAndExit(tracer, "Failed to mount after dehydrating.");
            }
        }

        private void PrepareSrcFolder(ITracer tracer, RGFSEnlistment enlistment)
        {
            string error;
            if (!GVFltCallbacks.TryPrepareFolderForGVFltCallbacks(enlistment.WorkingDirectoryRoot, out error))
            {
                this.WriteErrorAndExit(tracer, "Failed to recreate the virtualization root: " + error);
            }
        }

        private bool TryBackupFiles(ITracer tracer, RGFSEnlistment enlistment, string backupRoot)
        {
            string backupSrc = Path.Combine(backupRoot, "src");
            string backupGit = Path.Combine(backupRoot, ".git");
            string backupInfo = Path.Combine(backupGit, RGFSConstants.DotGit.Info.Name);
            string backupRgfs = Path.Combine(backupRoot, ".rgfs");
            string backupDatabases = Path.Combine(backupRgfs, RGFSConstants.DotRGFS.Databases.Name);

            string errorMessage = string.Empty;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    string ioError;
                    if (!this.TryIO(tracer, () => Directory.CreateDirectory(backupRoot), "Create backup directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupGit), "Create backup .git directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupInfo), "Create backup .git\\info directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupRgfs), "Create backup .rgfs directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupDatabases), "Create backup .rgfs databases directory", out ioError))
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
                                Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.SparseCheckoutPath),
                                Path.Combine(backupInfo, RGFSConstants.DotGit.Info.SparseCheckoutName)),
                            "Backup the sparse-checkout file",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () =>
                            {
                                if (File.Exists(Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.AlwaysExcludePath)))
                                {
                                    File.Move(
                                        Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.AlwaysExcludePath),
                                        Path.Combine(backupInfo, RGFSConstants.DotGit.Info.AlwaysExcludeName));
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
                                Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.SparseCheckoutPath),
                                RGFSConstants.GitPathSeparatorString + RGFSConstants.SpecialGitFiles.GitAttributes + "\n"),
                            "Recreate a new sparse-checkout file",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup the .rgfs hydration-related data structures...
                    if (!this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.BackgroundGitOperations), 
                                Path.Combine(backupRgfs, RGFSConstants.DotRGFS.Databases.BackgroundGitOperations)),
                            "Backup the BackgroundGitUpdates database",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.PlaceholderList), 
                                Path.Combine(backupRgfs, RGFSConstants.DotRGFS.Databases.PlaceholderList)),
                            "Backup the PlaceholderList database",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup everything related to the .git\index...
                    if (!this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotGitRoot, RGFSConstants.DotGit.IndexName),
                                Path.Combine(backupGit, RGFSConstants.DotGit.IndexName)),
                            "Backup the git index",
                            out errorMessage) ||
                        !this.TryIO(
                            tracer,
                            () => File.Move(
                                Path.Combine(enlistment.DotRGFSRoot, GitIndexProjection.ProjectionIndexBackupName),
                                Path.Combine(backupRgfs, GitIndexProjection.ProjectionIndexBackupName)),
                            "Backup RGFS_projection",
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

        private bool TryDownloadGitObjects(ITracer tracer, RGFSEnlistment enlistment)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    RetryConfig retryConfig;
                    if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out errorMessage))
                    {
                        errorMessage = "Failed to determine RGFS timeout and max retries: " + errorMessage;
                        return false;
                    }

                    CacheServerInfo cacheServer = new CacheServerInfo(enlistment.RepoUrl, null);
                    using (GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cacheServer, retryConfig))
                    {
                        PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                        GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem);
                        RGFSGitObjects gitObjects = new RGFSGitObjects(new RGFSContext(tracer, fileSystem, gitRepo, enlistment), objectRequestor);

                        GitProcess.Result revParseResult = enlistment.CreateGitProcess().RevParse("HEAD");
                        if (revParseResult.HasErrors)
                        {
                            errorMessage = "Unable to determine HEAD commit id: " + revParseResult.Errors;
                            return false;
                        }

                        string headCommit = revParseResult.Output.TrimEnd('\n');

                        if (!this.TryDownloadCommit(headCommit, enlistment, objectRequestor, gitObjects, gitRepo, out errorMessage) ||
                            !this.TryDownloadRootGitAttributes(enlistment, gitObjects, gitRepo, out errorMessage))
                        {
                            return false;
                        }
                    }

                    return true;
                },
                "Downloading git objects",
                suppressRgfsLogMessage: true))
            {
                this.WriteMessage(tracer, errorMessage);
                return false;
            }

            return true;
        }

        private bool TryRecreateIndex(ITracer tracer, RGFSEnlistment enlistment)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    // Create a new index based on the new minimal sparse-checkout
                    using (NamedPipeServer pipeServer = AllowAllLocksNamedPipeServer.Create(tracer, enlistment))
                    {
                        GitProcess git = new GitProcess(enlistment);
                        GitProcess.Result checkoutResult = git.ForceCheckout("HEAD");

                        errorMessage = checkoutResult.Errors;
                        return !checkoutResult.HasErrors;
                    }
                },
                "Recreating git index",
                suppressRgfsLogMessage: true))
            {
                this.WriteMessage(tracer, "Failed to recreate index: " + errorMessage);
                return false;
            }

            return true;
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            this.Output.WriteLine(message);
            tracer.RelatedEvent(
                EventLevel.Informational,
                "Dehydrate",
                new EventMetadata
                {
                    { TracingConstants.MessageKey.InfoMessage, message }
                });
        }

        private void WriteErrorAndExit(ITracer tracer, string message)
        {
            this.ReportErrorAndExit(tracer, "ERROR: " + message);
        }

        private ReturnCode ExecuteRGFSVerb<TVerb>(ITracer tracer)
            where TVerb : RGFSVerb, new()
        {
            try
            {
                ReturnCode returnCode;
                StringBuilder commandOutput = new StringBuilder();
                using (StringWriter writer = new StringWriter(commandOutput))
                {
                    returnCode = this.Execute<TVerb>(this.EnlistmentRootPath, verb => verb.Output = writer);
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
                    },
                    "ExecuteRGFSVerb: Caught exception");

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
                        { "Error", error }
                    },
                    "TryIO: Caught exception performing action");
            }

            return false;
        }
    }
}
