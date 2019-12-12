using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Maintenance;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using GVFS.Virtualization.Projection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.CommandLine
{
    [Verb(DehydrateVerb.DehydrateVerbName, HelpText = "EXPERIMENTAL FEATURE - Fully dehydrate a GVFS repo")]
    public class DehydrateVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string DehydrateVerbName = "dehydrate";
        private const string FolderListSeparator = ";";

        private PhysicalFileSystem fileSystem = new PhysicalFileSystem();

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
            HelpText = "Do not require a clean git status when dehydrating. To prevent data loss, this option cannot be combined with --folders option.")]
        public bool NoStatus { get; set; }

        [Option(
            "folders",
            Default = "",
            Required = false,
            HelpText = "A semicolon (" + FolderListSeparator + ") delimited list of folders to dehydrate. Each folder must be relative to the repository root.")]
        public string Folders { get; set; }

        public string RunningVerbName { get; set; } = DehydrateVerbName;
        public string ActionName { get; set; } = DehydrateVerbName;

        /// <summary>
        /// True if another verb (e.g. 'gvfs sparse') has already validated that status is clean
        /// </summary>
        public bool StatusChecked { get; set; }

        protected override string VerbName
        {
            get { return DehydrateVerb.DehydrateVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "Dehydrate"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Dehydrate),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    CacheServerResolver.GetUrlFromConfig(enlistment),
                    new EventMetadata
                    {
                        { "Confirmed", this.Confirmed },
                        { "NoStatus", this.NoStatus },
                        { "NamedPipeName", enlistment.NamedPipeName },
                        { "Folders", this.Folders },
                        { nameof(this.EnlistmentRootPathParameter), this.EnlistmentRootPathParameter },
                    });

                // This is only intended to be run by functional tests
                if (this.MaintenanceJob != null)
                {
                    this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig: null, serverGVFSConfig: null, cacheServer: null);
                    PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                    using (GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem))
                    using (GVFSContext context = new GVFSContext(tracer, fileSystem, gitRepo, enlistment))
                    {
                        switch (this.MaintenanceJob)
                        {
                            case "LooseObjects":
                                (new LooseObjectsStep(context, forceRun: true)).Execute();
                                return;

                            case "PackfileMaintenance":
                                (new PackfileMaintenanceStep(
                                    context,
                                    forceRun: true,
                                    batchSize: this.PackfileMaintenanceBatchSize ?? PackfileMaintenanceStep.DefaultBatchSize)).Execute();
                                return;

                            case "PostFetch":
                                (new PostFetchStep(context, new System.Collections.Generic.List<string>(), requireObjectCacheLock: false)).Execute();
                                return;

                            default:
                                this.ReportErrorAndExit($"Unknown maintenance job requested: {this.MaintenanceJob}");
                                break;
                        }
                    }
                }

                bool fullDehydrate = string.IsNullOrEmpty(this.Folders);

                if (!this.Confirmed && fullDehydrate)
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

To actually execute the dehydrate, run 'gvfs dehydrate --confirm' from the parent 
of your enlistment's src folder.
");

                    return;
                }
                else if (!this.Confirmed)
                {
                    this.Output.WriteLine(
@"WARNING: THIS IS AN EXPERIMENTAL FEATURE

All of your downloaded objects, branches, and siblings of the src folder
will be preserved.  This will remove the folders specified and any working directory
files and folders even if ignored by git similar to 'git clean -xdf <path>'.

Before you dehydrate, you will have to commit any working directory changes 
you want to keep and have a clean 'git status'.

To actually execute the dehydrate, run 'gvfs dehydrate --confirm --folders <folder list>'
from a parent of the folders list.
");

                    return;
                }

                if (this.NoStatus && !fullDehydrate)
                {
                    this.ReportErrorAndExit(tracer, "Dehydrate --no-status not valid with --folders");
                    return;
                }

                bool cleanStatus = this.StatusChecked || this.CheckGitStatus(tracer, enlistment, fullDehydrate);

                string backupRoot = Path.GetFullPath(Path.Combine(enlistment.EnlistmentRoot, "dehydrate_backup", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                this.Output.WriteLine();

                if (fullDehydrate)
                {
                    this.WriteMessage(tracer, $"Starting {this.RunningVerbName}. All of your existing files will be backed up in " + backupRoot);
                }

                this.WriteMessage(tracer, $"WARNING: If you abort the {this.RunningVerbName} after this point, the repo may become corrupt");

                this.Output.WriteLine();

                this.Unmount(tracer);

                string error;
                if (!DiskLayoutUpgrade.TryCheckDiskLayoutVersion(tracer, enlistment.EnlistmentRoot, out error))
                {
                    this.ReportErrorAndExit(tracer, error);
                }

                if (fullDehydrate)
                {
                    RetryConfig retryConfig;
                    if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                    {
                        this.ReportErrorAndExit(tracer, "Failed to determine GVFS timeout and max retries: " + error);
                    }

                    string errorMessage;
                    if (!this.TryAuthenticate(tracer, enlistment, out errorMessage))
                    {
                        this.ReportErrorAndExit(tracer, errorMessage);
                    }

                    // Local cache and objects paths are required for TryDownloadGitObjects
                    this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig, serverGVFSConfig: null, cacheServer: null);

                    this.RunFullDehydrate(tracer, enlistment, backupRoot, retryConfig);
                }
                else
                {
                    string[] folders = this.Folders.Split(new[] { FolderListSeparator }, StringSplitOptions.RemoveEmptyEntries);

                    if (folders.Length > 0)
                    {
                        if (cleanStatus)
                        {
                            this.DehydrateFolders(tracer, enlistment, folders);
                        }
                        else
                        {
                            this.ReportErrorAndExit($"Cannot {this.ActionName}: must have a clean git status.");
                        }
                    }
                    else
                    {
                        this.ReportErrorAndExit($"No folders to {this.ActionName}.");
                    }
                }
            }
        }

        private void DehydrateFolders(JsonTracer tracer, GVFSEnlistment enlistment, string[] folders)
        {
            List<string> foldersToDehydrate = new List<string>();
            List<string> folderErrors = new List<string>();

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    if (!ModifiedPathsDatabase.TryLoadOrCreate(
                            tracer,
                            Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths),
                            this.fileSystem,
                            out ModifiedPathsDatabase modifiedPaths,
                            out string error))
                    {
                        this.WriteMessage(tracer, $"Unable to open modified paths database: {error}");
                        return false;
                    }

                    using (modifiedPaths)
                    {
                        string ioError;
                        foreach (string folder in folders)
                        {
                            string normalizedPath = GVFSDatabase.NormalizePath(folder);
                            if (!this.IsFolderValid(normalizedPath))
                            {
                                this.WriteMessage(tracer, $"Cannot {this.ActionName} folder '{folder}': invalid folder path.");
                            }
                            else
                            {
                                // Need to check if parent folder is in the modified paths because
                                // dehydration will not do any good with a parent folder there
                                if (modifiedPaths.ContainsParentFolder(folder, out string parentFolder))
                                {
                                    this.WriteMessage(tracer, $"Cannot {this.ActionName} folder '{folder}': Must {this.ActionName} parent folder '{parentFolder}'.");
                                }
                                else
                                {
                                    string fullPath = Path.Combine(enlistment.WorkingDirectoryBackingRoot, folder);
                                    if (this.fileSystem.DirectoryExists(fullPath))
                                    {
                                        if (!this.TryIO(tracer, () => this.fileSystem.DeleteDirectory(fullPath), $"Deleting '{fullPath}'", out ioError))
                                        {
                                            this.WriteMessage(tracer, $"Cannot {this.ActionName} folder '{folder}': removing '{folder}' failed.");
                                            this.WriteMessage(tracer, "Ensure no applications are accessing the folder and retry.");
                                            this.WriteMessage(tracer, $"More details: {ioError}");
                                            folderErrors.Add($"{folder}\0{ioError}");
                                        }
                                        else
                                        {
                                            foldersToDehydrate.Add(folder);
                                        }
                                    }
                                    else
                                    {
                                        this.WriteMessage(tracer, $"Cannot {this.ActionName} folder '{folder}': '{folder}' does not exist.");

                                        // Still add to foldersToDehydrate so that any placeholders or modified paths get cleaned up
                                        foldersToDehydrate.Add(folder);
                                    }
                                }
                            }
                        }
                    }

                    return true;
                },
                "Cleaning up folders"))
            {
                this.ReportErrorAndExit(tracer, $"{this.ActionName} for folders failed.");
            }

            // We can skip the version check because dehydrating folders requires that a git status
            // be run first, and running git status requires that the repo already be mounted (meaning
            // we don't need to perform another version check again)
            this.Mount(
                tracer,
                skipVersionCheck: true);

            if (foldersToDehydrate.Count > 0)
            {
                this.SendDehydrateMessage(tracer, enlistment, folderErrors, foldersToDehydrate);
            }

            if (folderErrors.Count > 0)
            {
                foreach (string folderError in folderErrors)
                {
                    this.ErrorOutput.WriteLine(folderError);
                }

                this.ReportErrorAndExit(tracer, ReturnCode.DehydrateFolderFailures, $"Failed to dehydrate {folderErrors.Count} folder(s).");
            }
        }

        private bool IsFolderValid(string folderPath)
        {
            if (folderPath == GVFSConstants.DotGit.Root ||
                folderPath.StartsWith(GVFSConstants.DotGit.Root + Path.DirectorySeparatorChar) ||
                folderPath.StartsWith(".." + Path.DirectorySeparatorChar) ||
                folderPath.Contains(Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar) ||
                Path.GetInvalidPathChars().Any(invalidChar => folderPath.Contains(invalidChar)))
            {
                return false;
            }

            return true;
        }

        private void SendDehydrateMessage(ITracer tracer, GVFSEnlistment enlistment, List<string> folderErrors, List<string> folders)
        {
            NamedPipeMessages.DehydrateFolders.Response response = null;

            try
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                {
                    if (!pipeClient.Connect())
                    {
                        this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                    }

                    NamedPipeMessages.DehydrateFolders.Request request = new NamedPipeMessages.DehydrateFolders.Request(string.Join(FolderListSeparator, folders));
                    pipeClient.SendRequest(request.CreateMessage());
                    response = NamedPipeMessages.DehydrateFolders.Response.FromMessage(NamedPipeMessages.Message.FromString(pipeClient.ReadRawResponse()));
                }
            }
            catch (BrokenPipeException e)
            {
                this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
            }

            if (response != null)
            {
                foreach (string folder in response.SuccessfulFolders)
                {
                    this.WriteMessage(tracer, $"{folder} folder {this.ActionName} successful.");
                }

                foreach (string folder in response.FailedFolders)
                {
                    this.WriteMessage(tracer, $"{folder} folder failed to {this.ActionName}. You may need to reset the working directory by deleting {folder}, running `git reset --hard`, and retry the {this.ActionName}.");
                    folderErrors.Add(folder);
                }
            }
        }

        private void RunFullDehydrate(JsonTracer tracer, GVFSEnlistment enlistment, string backupRoot, RetryConfig retryConfig)
        {
            if (this.TryBackupFiles(tracer, enlistment, backupRoot))
            {
                if (this.TryDownloadGitObjects(tracer, enlistment, retryConfig) &&
                    this.TryRecreateIndex(tracer, enlistment))
                {
                    // Converting the src folder to partial must be the final step before mount
                    this.PrepareSrcFolder(tracer, enlistment);

                    // We can skip the version check if git status was run because git status requires
                    // that the repo already be mounted (meaning we don't need to perform another version check again)
                    this.Mount(
                        tracer,
                        skipVersionCheck: !this.NoStatus);

                    this.Output.WriteLine();
                    this.WriteMessage(tracer, "The repo was successfully dehydrated and remounted");
                }
            }
            else
            {
                this.Output.WriteLine();
                this.WriteMessage(tracer, "ERROR: Backup failed. We will attempt to mount, but you may need to reclone if that fails");

                // We can skip the version check if git status was run because git status requires
                // that the repo already be mounted (meaning we don't need to perform another version check again)
                this.Mount(
                        tracer,
                        skipVersionCheck: !this.NoStatus);

                this.WriteMessage(tracer, "Dehydrate failed, but remounting succeeded");
            }
        }

        private void Mount(ITracer tracer, bool skipVersionCheck)
        {
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    return this.ExecuteGVFSVerb<MountVerb>(
                        tracer,
                        verb =>
                        {
                            verb.SkipInstallHooks = true;
                            verb.SkipVersionCheck = skipVersionCheck;
                            verb.SkipMountedCheck = true;
                        }) == ReturnCode.Success;
                },
                "Mounting"))
            {
                this.ReportErrorAndExit(tracer, "Failed to mount.");
            }
        }

        private bool CheckGitStatus(ITracer tracer, GVFSEnlistment enlistment, bool fullDehydrate)
        {
            if (!this.NoStatus)
            {
                this.WriteMessage(tracer, $"Running git status before {this.ActionName} to make sure you don't have any pending changes.");
                if (fullDehydrate)
                {
                    this.WriteMessage(tracer, $"If this takes too long, you can abort and run {this.RunningVerbName} with --no-status to skip this safety check.");
                }

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
                        statusResult = git.Status(allowObjectDownloads: false, useStatusCache: false, showUntracked: true);
                        if (statusResult.ExitCodeIsFailure)
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
                        if (fullDehydrate)
                        {
                            this.WriteMessage(tracer, "Either mount first, or run with --no-status");
                        }
                    }
                    else if (statusResult.ExitCodeIsFailure)
                    {
                        this.WriteMessage(tracer, "Failed to run git status: " + statusResult.Errors);
                    }
                    else
                    {
                        this.WriteMessage(tracer, statusResult.Output);
                        this.WriteMessage(tracer, "git status reported that you have dirty files");
                        if (fullDehydrate)
                        {
                            this.WriteMessage(tracer, $"Either commit your changes or run {this.RunningVerbName} with --no-status");
                        }
                        else
                        {
                            this.WriteMessage(tracer, "Either commit your changes or reset and clean your working directory.");
                        }
                    }

                    this.ReportErrorAndExit(tracer, $"Aborted {this.ActionName}");
                    return false;
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        private void PrepareSrcFolder(ITracer tracer, GVFSEnlistment enlistment)
        {
            Exception exception;
            string error;
            if (!GVFSPlatform.Instance.KernelDriver.TryPrepareFolderForCallbacks(enlistment.WorkingDirectoryBackingRoot, out error, out exception))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(error), error);
                if (exception != null)
                {
                    metadata.Add("Exception", exception.ToString());
                }

                tracer.RelatedError(metadata, $"{nameof(this.PrepareSrcFolder)}: TryPrepareFolderForCallbacks failed");
                this.ReportErrorAndExit(tracer, "Failed to recreate the virtualization root: " + error);
            }
        }

        private bool TryBackupFiles(ITracer tracer, GVFSEnlistment enlistment, string backupRoot)
        {
            string backupSrc = Path.Combine(backupRoot, "src");
            string backupGit = Path.Combine(backupRoot, ".git");
            string backupGvfs = Path.Combine(backupRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            string backupDatabases = Path.Combine(backupGvfs, GVFSConstants.DotGVFS.Databases.Name);

            string errorMessage = string.Empty;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    string ioError;
                    if (!this.TryIO(tracer, () => Directory.CreateDirectory(backupRoot), "Create backup directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupGit), "Create backup .git directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupGvfs), "Create backup .gvfs directory", out ioError) ||
                        !this.TryIO(tracer, () => Directory.CreateDirectory(backupDatabases), "Create backup .gvfs databases directory", out ioError))
                    {
                        errorMessage = "Failed to create backup folders at " + backupRoot + ": " + ioError;
                        return false;
                    }

                    // Move the current src folder to the backup location...
                    if (!this.TryIO(tracer, () => Directory.Move(enlistment.WorkingDirectoryBackingRoot, backupSrc), "Move the src folder", out ioError))
                    {
                        errorMessage = "Failed to move the src folder: " + ioError + Environment.NewLine;
                        errorMessage += "Make sure you have no open handles or running processes in the src folder";
                        return false;
                    }

                    // ... but move the .git folder back to the new src folder so we can preserve objects, refs, logs...
                    if (!this.TryIO(tracer, () => Directory.CreateDirectory(enlistment.WorkingDirectoryBackingRoot), "Create new src folder", out errorMessage) ||
                        !this.TryIO(tracer, () => Directory.Move(Path.Combine(backupSrc, ".git"), enlistment.DotGitRoot), "Keep existing .git folder", out errorMessage))
                    {
                        return false;
                    }

                    // ... backup the .gvfs hydration-related data structures...
                    string databasesFolder = Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.Name);
                    if (!this.TryBackupFilesInFolder(tracer, databasesFolder, backupDatabases, searchPattern: "*", filenamesToSkip: "RepoMetadata.dat"))
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
                                Path.Combine(enlistment.DotGVFSRoot, GitIndexProjection.ProjectionIndexBackupName),
                                Path.Combine(backupGvfs, GitIndexProjection.ProjectionIndexBackupName)),
                            "Backup GVFS_projection",
                            out errorMessage))
                    {
                        return false;
                    }

                    // ... backup all .git\*.lock files
                    if (!this.TryBackupFilesInFolder(tracer, enlistment.DotGitRoot, backupGit, searchPattern: "*.lock"))
                    {
                        return false;
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

        private bool TryBackupFilesInFolder(ITracer tracer, string folderPath, string backupPath, string searchPattern, params string[] filenamesToSkip)
        {
            string errorMessage;
            foreach (string file in Directory.GetFiles(folderPath, searchPattern))
            {
                string fileName = Path.GetFileName(file);
                if (!filenamesToSkip.Any(x => x.Equals(fileName, GVFSPlatform.Instance.Constants.PathComparison)))
                {
                    if (!this.TryIO(
                        tracer,
                        () => File.Move(file, file.Replace(folderPath, backupPath)),
                        $"Backing up {Path.GetFileName(file)}",
                        out errorMessage))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryDownloadGitObjects(ITracer tracer, GVFSEnlistment enlistment, RetryConfig retryConfig)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    CacheServerInfo cacheServer = new CacheServerInfo(enlistment.RepoUrl, null);
                    using (GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cacheServer, retryConfig))
                    {
                        PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                        GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem);
                        GVFSGitObjects gitObjects = new GVFSGitObjects(new GVFSContext(tracer, fileSystem, gitRepo, enlistment), objectRequestor);

                        GitProcess.Result revParseResult = enlistment.CreateGitProcess().RevParse("HEAD");
                        if (revParseResult.ExitCodeIsFailure)
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
                suppressGvfsLogMessage: true))
            {
                this.WriteMessage(tracer, errorMessage);
                return false;
            }

            return true;
        }

        private bool TryRecreateIndex(ITracer tracer, GVFSEnlistment enlistment)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    // Create a new index based on the new minimal modified paths
                    using (NamedPipeServer pipeServer = AllowAllLocksNamedPipeServer.Create(tracer, enlistment))
                    {
                        GitProcess git = new GitProcess(enlistment);
                        GitProcess.Result checkoutResult = git.ForceCheckout("HEAD");

                        errorMessage = checkoutResult.Errors;
                        return checkoutResult.ExitCodeIsSuccess;
                    }
                },
                "Recreating git index",
                suppressGvfsLogMessage: true))
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
