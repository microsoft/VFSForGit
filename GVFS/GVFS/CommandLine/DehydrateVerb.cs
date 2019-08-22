using CommandLine;
using GVFS.Common;
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
            HelpText = "Skip 'git status' before dehydrating only valid when not using folders option")]
        public bool NoStatus { get; set; }

        [Option(
            "folders",
            Default = "",
            Required = false,
            HelpText = "The folders to dehydrate separated by ;")]
        public string Folders { get; set; }

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
will be preserved. Your modified working directory files in the folders specified
will be moved to the backup.

Before you dehydrate, you will have to commit any working directory changes 
you want to keep and have a clean 'git status'.

To actually execute the dehydrate, run 'gvfs dehydrate --confirm --folders <folder list>'
from a parent of the folders list.
");

                    return;
                }

                bool cleanStatus = this.CheckGitStatus(tracer, enlistment, fullDehydrate);

                string backupRoot = Path.GetFullPath(Path.Combine(enlistment.EnlistmentRoot, "dehydrate_backup", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                this.Output.WriteLine();

                if (fullDehydrate)
                {
                    this.WriteMessage(tracer, "Starting dehydration. All of your existing files will be backed up in " + backupRoot);
                }
                else
                {
                    this.WriteMessage(tracer, "Starting dehydration. Folders specified will be backed up in " + backupRoot);
                }

                this.WriteMessage(tracer, "WARNING: If you abort the dehydrate after this point, the repo may become corrupt");

                this.Output.WriteLine();

                this.Unmount(tracer);

                string error;
                if (!DiskLayoutUpgrade.TryCheckDiskLayoutVersion(tracer, enlistment.EnlistmentRoot, out error))
                {
                    this.ReportErrorAndExit(tracer, error);
                }

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

                if (string.IsNullOrEmpty(this.Folders))
                {
                    this.RunFullDehydrate(tracer, enlistment, backupRoot, retryConfig);
                }
                else
                {
                    string[] folders = this.Folders.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);

                    if (folders.Length > 0)
                    {
                        if (cleanStatus)
                        {
                            this.DehydrateFolders(tracer, enlistment, backupRoot, folders);
                        }
                        else
                        {
                            this.ReportErrorAndExit("Must have a clean git status to dehydrate folders.");
                        }
                    }
                    else
                    {
                        this.ReportErrorAndExit("No folders to dehydrate.");
                    }
                }
            }
        }

        private void DehydrateFolders(JsonTracer tracer, GVFSEnlistment enlistment, string backupRoot, string[] folders)
        {
            List<string> foldersToDehydrate = new List<string>();

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
                            // Need to check if parent folder is in the modified paths because
                            // dehydration will not do any good with a parent folder there
                            if (modifiedPaths.ContainsParentFolder(folder))
                            {
                                this.WriteMessage(tracer, $"Unable to dehydrate {folder}. Parent folder in modified paths that must be dehydrated.");
                            }
                            else
                            {
                                string fullPath = Path.Combine(enlistment.WorkingDirectoryRoot, folder);
                                string backupPath = Path.Combine(backupRoot, folder);
                                if (this.fileSystem.DirectoryExists(fullPath))
                                {
                                    if (!this.TryIO(tracer, () => this.fileSystem.CopyDirectoryRecursive(fullPath, backupPath), $"Backing up {folder} to {backupPath}", out ioError))
                                    {
                                        this.WriteMessage(tracer, $"Copying files to backup location failed for {folder} and will not be dehydrated. {ioError}");
                                        this.WriteMessage(tracer, $"Make sure there aren't any applications accessing the folder and try again.");
                                    }
                                    else
                                    {
                                        if (!this.TryIO(tracer, () => this.fileSystem.DeleteDirectory(fullPath), $"Deleting {fullPath}", out ioError))
                                        {
                                            this.WriteMessage(tracer, $"Removing {folder} failed and will not be dehydrated. {ioError}");
                                            this.WriteMessage(tracer, $"Make sure there aren't any applications accessing the folder and try again.");
                                        }
                                        else
                                        {
                                            foldersToDehydrate.Add(folder);
                                        }
                                    }
                                }
                                else
                                {
                                    this.WriteMessage(tracer, $"{folder} did not exist to dehydrate.");
                                }
                            }
                        }
                    }

                    return true;
                },
                "Backing up folders"))
            {
                this.ReportErrorAndExit(tracer, "Dehydrate for folders failed.");
            }

            this.Mount(tracer);

            this.SendDehydrateMessage(tracer, enlistment, backupRoot, foldersToDehydrate.ToArray());
        }

        private void SendDehydrateMessage(ITracer tracer, GVFSEnlistment enlistment, string backupRoot, string[] folders)
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

                    NamedPipeMessages.DehydrateFolders.Request request = new NamedPipeMessages.DehydrateFolders.Request(string.Join(";", folders));
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
                    this.WriteMessage(tracer, $"{folder} folder successfully dehydrated.");
                }

                foreach (string folder in response.FailedFolders)
                {
                    this.WriteMessage(tracer, $"{folder} folder failed to dehydrate. You may need to reset the working directory by deleting {folder}, running `git reset --hard`, and retry the dehydrate.");
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

        private bool CheckGitStatus(ITracer tracer, GVFSEnlistment enlistment, bool fullDehydrate)
        {
            if (!this.NoStatus)
            {
                this.WriteMessage(tracer, "Running git status before dehydrating to make sure you don't have any pending changes.");
                if (fullDehydrate)
                {
                    this.WriteMessage(tracer, "If this takes too long, you can abort and run dehydrate with --no-status to skip this safety check.");
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
                            this.WriteMessage(tracer, "Either commit your changes or run dehydrate with --no-status");
                        }
                        else
                        {
                            this.WriteMessage(tracer, "Either commit your changes or reset and clean your working directory.");
                        }
                    }

                    this.ReportErrorAndExit(tracer, "Dehydrate was aborted");
                    return false;
                }
                else
                {
                    return true;
                }
            }

            return false;
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
                this.ReportErrorAndExit(tracer, "Unable to unmount.");
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
                this.ReportErrorAndExit(tracer, "Failed to mount after dehydrating.");
            }
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

        private ReturnCode ExecuteGVFSVerb<TVerb>(ITracer tracer)
            where TVerb : GVFSVerb, new()
        {
            try
            {
                ReturnCode returnCode;
                StringBuilder commandOutput = new StringBuilder();
                using (StringWriter writer = new StringWriter(commandOutput))
                {
                    returnCode = this.Execute<TVerb>(this.EnlistmentRootPathParameter, verb => verb.Output = writer);
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
                    "ExecuteGVFSVerb: Caught exception");

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
