using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GVFS.CommandLine
{
    [Verb(CloneVerb.CloneVerbName, HelpText = "Clone a git repo and mount it as a GVFS virtual repo")]
    public class CloneVerb : GVFSVerb
    {
        private const string CloneVerbName = "clone";

        [Value(
            0,
            Required = true,
            MetaName = "Repository URL",
            HelpText = "The url of the repo")]
        public string RepositoryURL { get; set; }

        [Value(
            1,
            Required = false,
            Default = "",
            MetaName = "Enlistment Root Path",
            HelpText = "Full or relative path to the GVFS enlistment root")]
        public override string EnlistmentRootPathParameter { get; set; }

        [Option(
            "cache-server-url",
            Required = false,
            Default = null,
            HelpText = "The url or friendly name of the cache server")]
        public string CacheServerUrl { get; set; }

        [Option(
            'b',
            "branch",
            Required = false,
            HelpText = "Branch to checkout after clone")]
        public string Branch { get; set; }

        [Option(
            "single-branch",
            Required = false,
            Default = false,
            HelpText = "Use this option to only download metadata for the branch that will be checked out")]
        public bool SingleBranch { get; set; }

        [Option(
            "no-mount",
            Required = false,
            Default = false,
            HelpText = "Use this option to only clone, but not mount the repo")]
        public bool NoMount { get; set; }

        [Option(
            "no-prefetch",
            Required = false,
            Default = false,
            HelpText = "Use this option to not prefetch commits after clone")]
        public bool NoPrefetch { get; set; }

        [Option(
            "local-cache-path",
            Required = false,
            HelpText = "Use this option to override the path for the local GVFS cache.")]
        public string LocalCacheRoot { get; set; }

        protected override string VerbName
        {
            get { return CloneVerbName; }
        }

        public override void Execute()
        {
            int exitCode = 0;

            this.ValidatePathParameter(this.EnlistmentRootPathParameter);
            this.ValidatePathParameter(this.LocalCacheRoot);

            string fullEnlistmentRootPathParameter;
            string normalizedEnlistmentRootPath = this.GetCloneRoot(out fullEnlistmentRootPathParameter);

            if (!string.IsNullOrWhiteSpace(this.LocalCacheRoot))
            {
                string fullLocalCacheRootPath = Path.GetFullPath(this.LocalCacheRoot);

                string errorMessage;
                string normalizedLocalCacheRootPath;
                if (!GVFSPlatform.Instance.FileSystem.TryGetNormalizedPath(fullLocalCacheRootPath, out normalizedLocalCacheRootPath, out errorMessage))
                {
                    this.ReportErrorAndExit($"Failed to determine normalized path for '--local-cache-path' path {fullLocalCacheRootPath}: {errorMessage}");
                }

                if (normalizedLocalCacheRootPath.StartsWith(
                    Path.Combine(normalizedEnlistmentRootPath, GVFSConstants.WorkingDirectoryRootName),
                    GVFSPlatform.Instance.Constants.PathComparison))
                {
                    this.ReportErrorAndExit("'--local-cache-path' cannot be inside the src folder");
                }
            }

            this.CheckKernelDriverSupported(normalizedEnlistmentRootPath);
            this.CheckNotInsideExistingRepo(normalizedEnlistmentRootPath);
            this.BlockEmptyCacheServerUrl(this.CacheServerUrl);

            try
            {
                GVFSEnlistment enlistment;
                Result cloneResult = new Result(false);

                CacheServerInfo cacheServer = null;
                ServerGVFSConfig serverGVFSConfig = null;

                using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "GVFSClone"))
                {
                    cloneResult = this.TryCreateEnlistment(fullEnlistmentRootPathParameter, normalizedEnlistmentRootPath, out enlistment);
                    if (cloneResult.Success)
                    {
                        // Create the enlistment root explicitly with CreateDirectoryAccessibleByAuthUsers before calling
                        // AddLogFileEventListener to ensure that elevated and non-elevated users have access to the root.
                        string createDirectoryError;
                        if (!GVFSPlatform.Instance.FileSystem.TryCreateDirectoryAccessibleByAuthUsers(enlistment.EnlistmentRoot, out createDirectoryError))
                        {
                            this.ReportErrorAndExit($"Failed to create '{enlistment.EnlistmentRoot}': {createDirectoryError}");
                        }

                        tracer.AddLogFileEventListener(
                            GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Clone),
                            EventLevel.Informational,
                            Keywords.Any);
                        tracer.WriteStartEvent(
                            enlistment.EnlistmentRoot,
                            enlistment.RepoUrl,
                            this.CacheServerUrl,
                            new EventMetadata
                            {
                                { "Branch", this.Branch },
                                { "LocalCacheRoot", this.LocalCacheRoot },
                                { "SingleBranch", this.SingleBranch },
                                { "NoMount", this.NoMount },
                                { "NoPrefetch", this.NoPrefetch },
                                { "Unattended", this.Unattended },
                                { "IsElevated", GVFSPlatform.Instance.IsElevated() },
                                { "NamedPipeName", enlistment.NamedPipeName },
                                { "ProcessID", Process.GetCurrentProcess().Id },
                                { nameof(this.EnlistmentRootPathParameter), this.EnlistmentRootPathParameter },
                                { nameof(fullEnlistmentRootPathParameter), fullEnlistmentRootPathParameter },
                            });

                        CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                        cacheServer = cacheServerResolver.ParseUrlOrFriendlyName(this.CacheServerUrl);

                        string resolvedLocalCacheRoot;
                        if (string.IsNullOrWhiteSpace(this.LocalCacheRoot))
                        {
                            string localCacheRootError;
                            if (!LocalCacheResolver.TryGetDefaultLocalCacheRoot(enlistment, out resolvedLocalCacheRoot, out localCacheRootError))
                            {
                                this.ReportErrorAndExit(
                                    tracer,
                                    $"Failed to determine the default location for the local GVFS cache: `{localCacheRootError}`");
                            }
                        }
                        else
                        {
                            resolvedLocalCacheRoot = Path.GetFullPath(this.LocalCacheRoot);
                        }

                        this.Output.WriteLine("Clone parameters:");
                        this.Output.WriteLine("  Repo URL:     " + enlistment.RepoUrl);
                        this.Output.WriteLine("  Branch:       " + (string.IsNullOrWhiteSpace(this.Branch) ? "Default" : this.Branch));
                        this.Output.WriteLine("  Cache Server: " + cacheServer);
                        this.Output.WriteLine("  Local Cache:  " + resolvedLocalCacheRoot);
                        this.Output.WriteLine("  Destination:  " + enlistment.EnlistmentRoot);

                        string authErrorMessage;
                        if (!this.TryAuthenticate(tracer, enlistment, out authErrorMessage))
                        {
                            this.ReportErrorAndExit(tracer, "Cannot clone because authentication failed: " + authErrorMessage);
                        }

                        RetryConfig retryConfig = this.GetRetryConfig(tracer, enlistment, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));
                        serverGVFSConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);

                        cacheServer = this.ResolveCacheServer(tracer, cacheServer, cacheServerResolver, serverGVFSConfig);

                        this.ValidateClientVersions(tracer, enlistment, serverGVFSConfig, showWarnings: true);

                        this.ShowStatusWhileRunning(
                            () =>
                            {
                                cloneResult = this.TryClone(tracer, enlistment, cacheServer, retryConfig, serverGVFSConfig, resolvedLocalCacheRoot);
                                return cloneResult.Success;
                            },
                            "Cloning",
                            normalizedEnlistmentRootPath);
                    }

                    if (!cloneResult.Success)
                    {
                        tracer.RelatedError(cloneResult.ErrorMessage);
                    }
                }

                if (cloneResult.Success)
                {
                    if (!this.NoPrefetch)
                    {
                        bool trustPackIndexes = enlistment.GetTrustPackIndexesConfig();
                        /* If pack indexes are not trusted, the prefetch can take a long time.
                         * We will run the prefetch command in the background.
                         */
                        if (trustPackIndexes)
                        {
                            ReturnCode result = this.Execute<PrefetchVerb>(
                                enlistment,
                                verb =>
                                {
                                    verb.Commits = true;
                                    verb.SkipVersionCheck = true;
                                    verb.ResolvedCacheServer = cacheServer;
                                    verb.ServerGVFSConfig = serverGVFSConfig;
                                });

                            if (result != ReturnCode.Success)
                            {
                                this.Output.WriteLine("\r\nError during prefetch @ {0}", fullEnlistmentRootPathParameter);
                                exitCode = (int)result;
                            }
                        }

                        else
                        {
                            try
                            {
                                string gvfsExecutable = Assembly.GetExecutingAssembly().Location;
                                Process.Start(new ProcessStartInfo(
                                    fileName: gvfsExecutable,
                                    arguments: "prefetch --commits")
                                    {
                                        UseShellExecute = true,
                                        WindowStyle = ProcessWindowStyle.Hidden,
                                        WorkingDirectory = enlistment.EnlistmentRoot
                                    });
                                this.Output.WriteLine("\r\nPrefetch of commit graph has been started as a background process. Git operations involving history may be slower until prefetch has completed.\r\n");
                            }
                            catch (Win32Exception ex)
                            {
                                this.Output.WriteLine("\r\nError starting prefetch: " + ex.Message);
                                this.Output.WriteLine("Run 'gvfs prefetch --commits' from within your enlistment to prefetch the commit graph.");
                            }
                        }
                    }

                    if (this.NoMount)
                    {
                        this.Output.WriteLine("\r\nIn order to mount, first cd to within your enlistment, then call: ");
                        this.Output.WriteLine("gvfs mount");
                    }
                    else
                    {
                        this.Execute<MountVerb>(
                            enlistment,
                            verb =>
                            {
                                verb.SkipMountedCheck = true;
                                verb.SkipVersionCheck = true;
                                verb.ResolvedCacheServer = cacheServer;
                                verb.DownloadedGVFSConfig = serverGVFSConfig;
                            });
                    }
                }
                else
                {
                    this.Output.WriteLine("\r\nCannot clone @ {0}", fullEnlistmentRootPathParameter);
                    this.Output.WriteLine("Error: {0}", cloneResult.ErrorMessage);
                    exitCode = (int)ReturnCode.GenericError;
                }
            }
            catch (AggregateException e)
            {
                this.Output.WriteLine("Cannot clone @ {0}:", fullEnlistmentRootPathParameter);
                foreach (Exception ex in e.Flatten().InnerExceptions)
                {
                    this.Output.WriteLine("Exception: {0}", ex.ToString());
                }

                exitCode = (int)ReturnCode.GenericError;
            }
            catch (VerbAbortedException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.ReportErrorAndExit("Cannot clone @ {0}: {1}", fullEnlistmentRootPathParameter, e.ToString());
            }

            Environment.Exit(exitCode);
        }

        private static bool IsForceCheckoutErrorCloneFailure(string checkoutError)
        {
            if (string.IsNullOrWhiteSpace(checkoutError) ||
                checkoutError.Contains("Already on"))
            {
                return false;
            }

            return true;
        }

        private Result TryCreateEnlistment(
            string fullEnlistmentRootPathParameter,
            string normalizedEnlistementRootPath,
            out GVFSEnlistment enlistment)
        {
            enlistment = null;

            // Check that EnlistmentRootPath is empty before creating a tracer and LogFileEventListener as
            // LogFileEventListener will create a file in EnlistmentRootPath
            if (Directory.Exists(normalizedEnlistementRootPath) && Directory.EnumerateFileSystemEntries(normalizedEnlistementRootPath).Any())
            {
                if (fullEnlistmentRootPathParameter.Equals(normalizedEnlistementRootPath, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    return new Result($"Clone directory '{fullEnlistmentRootPathParameter}' exists and is not empty");
                }

                return new Result($"Clone directory '{fullEnlistmentRootPathParameter}' ['{normalizedEnlistementRootPath}'] exists and is not empty");
            }

            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                return new Result(GVFSConstants.GitIsNotInstalledError);
            }

            this.CheckGVFSHooksVersion(tracer: null, hooksVersion: out _);

            try
            {
                enlistment = new GVFSEnlistment(
                    normalizedEnlistementRootPath,
                    this.RepositoryURL,
                    gitBinPath,
                    authentication: null);
            }
            catch (InvalidRepoException e)
            {
                return new Result($"Error when creating a new GVFS enlistment at '{normalizedEnlistementRootPath}'. {e.Message}");
            }

            return new Result(true);
        }

        private Result TryClone(
            JsonTracer tracer,
            GVFSEnlistment enlistment,
            CacheServerInfo cacheServer,
            RetryConfig retryConfig,
            ServerGVFSConfig serverGVFSConfig,
            string resolvedLocalCacheRoot)
        {
            Result pipeResult;
            using (NamedPipeServer pipeServer = this.StartNamedPipe(tracer, enlistment, out pipeResult))
            {
                if (!pipeResult.Success)
                {
                    return pipeResult;
                }

                using (GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cacheServer, retryConfig))
                {
                    GitRefs refs = objectRequestor.QueryInfoRefs(this.SingleBranch ? this.Branch : null);

                    if (refs == null)
                    {
                        return new Result("Could not query info/refs from: " + Uri.EscapeUriString(enlistment.RepoUrl));
                    }

                    if (this.Branch == null)
                    {
                        this.Branch = refs.GetDefaultBranch();

                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Branch", this.Branch);
                        tracer.RelatedEvent(EventLevel.Informational, "CloneDefaultRemoteBranch", metadata);
                    }
                    else
                    {
                        if (!refs.HasBranch(this.Branch))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Branch", this.Branch);
                            tracer.RelatedEvent(EventLevel.Warning, "CloneBranchDoesNotExist", metadata);

                            string errorMessage = string.Format("Remote branch {0} not found in upstream origin", this.Branch);
                            return new Result(errorMessage);
                        }
                    }

                    if (!enlistment.TryCreateEnlistmentSubFolders())
                    {
                        string error = "Could not create enlistment directories";
                        tracer.RelatedError(error);
                        return new Result(error);
                    }

                    if (!GVFSPlatform.Instance.FileSystem.IsFileSystemSupported(enlistment.EnlistmentRoot, out string fsError))
                    {
                        string error = $"FileSystem unsupported: {fsError}";
                        tracer.RelatedError(error);
                        return new Result(error);
                    }

                    string localCacheError;
                    if (!this.TryDetermineLocalCacheAndInitializePaths(tracer, enlistment, serverGVFSConfig, cacheServer, resolvedLocalCacheRoot, out localCacheError))
                    {
                        tracer.RelatedError(localCacheError);
                        return new Result(localCacheError);
                    }

                    // There's no need to use CreateDirectoryAccessibleByAuthUsers as these directories will inherit
                    // the ACLs used to create LocalCacheRoot
                    Directory.CreateDirectory(enlistment.GitObjectsRoot);
                    Directory.CreateDirectory(enlistment.GitPackRoot);
                    Directory.CreateDirectory(enlistment.BlobSizesRoot);

                    return this.CreateClone(tracer, enlistment, objectRequestor, refs, this.Branch);
                }
            }
        }

        private NamedPipeServer StartNamedPipe(ITracer tracer, GVFSEnlistment enlistment, out Result errorResult)
        {
            try
            {
                errorResult = new Result(true);
                return AllowAllLocksNamedPipeServer.Create(tracer, enlistment);
            }
            catch (PipeNameLengthException)
            {
                errorResult = new Result("Failed to clone. Path exceeds the maximum number of allowed characters");
                return null;
            }
        }

        private string GetCloneRoot(out string fullEnlistmentRootPathParameter)
        {
            fullEnlistmentRootPathParameter = null;

            try
            {
                string repoName = this.RepositoryURL.Substring(this.RepositoryURL.LastIndexOf('/') + 1);
                fullEnlistmentRootPathParameter =
                    string.IsNullOrWhiteSpace(this.EnlistmentRootPathParameter)
                    ? Path.Combine(Environment.CurrentDirectory, repoName)
                    : this.EnlistmentRootPathParameter;

                fullEnlistmentRootPathParameter = Path.GetFullPath(fullEnlistmentRootPathParameter);

                string errorMessage;
                string enlistmentRootPath;
                if (!GVFSPlatform.Instance.FileSystem.TryGetNormalizedPath(fullEnlistmentRootPathParameter, out enlistmentRootPath, out errorMessage))
                {
                    this.ReportErrorAndExit("Unable to determine normalized path of clone root: " + errorMessage);
                    return null;
                }

                return enlistmentRootPath;
            }
            catch (IOException e)
            {
                this.ReportErrorAndExit("Unable to determine clone root: " + e.ToString());
                return null;
            }
        }

        private void CheckKernelDriverSupported(string normalizedEnlistmentRootPath)
        {
            string warning;
            string error;
            if (!GVFSPlatform.Instance.KernelDriver.IsSupported(normalizedEnlistmentRootPath, out warning, out error))
            {
                this.ReportErrorAndExit($"Error: {error}");
            }
            else if (!string.IsNullOrEmpty(warning))
            {
                this.Output.WriteLine();
                this.Output.WriteLine($"WARNING: {warning}");
            }
        }

        private void CheckNotInsideExistingRepo(string normalizedEnlistmentRootPath)
        {
            string errorMessage;
            string existingEnlistmentRoot;
            if (GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(normalizedEnlistmentRootPath, out existingEnlistmentRoot, out errorMessage))
            {
                this.ReportErrorAndExit("Error: You can't clone inside an existing GVFS repo ({0})", existingEnlistmentRoot);
            }

            if (this.IsExistingPipeListening(normalizedEnlistmentRootPath))
            {
                this.ReportErrorAndExit($"Error: There is currently a GVFS.Mount process running for '{normalizedEnlistmentRootPath}'. This process must be stopped before cloning.");
            }
        }

        private bool TryDetermineLocalCacheAndInitializePaths(
            ITracer tracer,
            GVFSEnlistment enlistment,
            ServerGVFSConfig serverGVFSConfig,
            CacheServerInfo currentCacheServer,
            string localCacheRoot,
            out string errorMessage)
        {
            errorMessage = null;
            LocalCacheResolver localCacheResolver = new LocalCacheResolver(enlistment);

            string error;
            string localCacheKey;
            if (!localCacheResolver.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers(
                tracer,
                serverGVFSConfig,
                currentCacheServer,
                localCacheRoot,
                localCacheKey: out localCacheKey,
                errorMessage: out error))
            {
                errorMessage = "Error determining local cache key: " + error;
                return false;
            }

            EventMetadata metadata = new EventMetadata();
            metadata.Add("localCacheRoot", localCacheRoot);
            metadata.Add("localCacheKey", localCacheKey);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, "Initializing cache paths");
            tracer.RelatedEvent(EventLevel.Informational, "CloneVerb_TryDetermineLocalCacheAndInitializePaths", metadata);

            enlistment.InitializeCachePathsFromKey(localCacheRoot, localCacheKey);

            return true;
        }

        private Result CreateClone(
            ITracer tracer,
            GVFSEnlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            GitRefs refs,
            string branch)
        {
            Result initRepoResult = this.TryInitRepo(tracer, refs, enlistment);
            if (!initRepoResult.Success)
            {
                return initRepoResult;
            }

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            string errorMessage;
            if (!this.TryCreateAlternatesFile(fileSystem, enlistment, out errorMessage))
            {
                return new Result("Error configuring alternate: " + errorMessage);
            }

            GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem);
            GVFSContext context = new GVFSContext(tracer, fileSystem, gitRepo, enlistment);
            GVFSGitObjects gitObjects = new GVFSGitObjects(context, objectRequestor);

            if (!this.TryDownloadCommit(
                refs.GetTipCommitId(branch),
                enlistment,
                objectRequestor,
                gitObjects,
                gitRepo,
                out errorMessage))
            {
                return new Result(errorMessage);
            }

            if (!GVFSVerb.TrySetRequiredGitConfigSettings(enlistment) ||
                !GVFSVerb.TrySetOptionalGitConfigSettings(enlistment))
            {
                return new Result("Unable to configure git repo");
            }

            CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
            if (!cacheServerResolver.TrySaveUrlToLocalConfig(objectRequestor.CacheServer, out errorMessage))
            {
                return new Result("Unable to configure cache server: " + errorMessage);
            }

            GitProcess git = new GitProcess(enlistment);
            string originBranchName = "origin/" + branch;
            GitProcess.Result createBranchResult = git.CreateBranchWithUpstream(branch, originBranchName);
            if (createBranchResult.ExitCodeIsFailure)
            {
                return new Result("Unable to create branch '" + originBranchName + "': " + createBranchResult.Errors + "\r\n" + createBranchResult.Output);
            }

            File.WriteAllText(
                Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Head),
                "ref: refs/heads/" + branch);

            if (!this.TryDownloadRootGitAttributes(enlistment, gitObjects, gitRepo, out errorMessage))
            {
                return new Result(errorMessage);
            }

            this.CreateGitScript(enlistment);

            string installHooksError;
            if (!HooksInstaller.InstallHooks(context, out installHooksError))
            {
                tracer.RelatedError(installHooksError);
                return new Result(installHooksError);
            }

            GitProcess.Result forceCheckoutResult = git.ForceCheckout(branch);
            if (forceCheckoutResult.ExitCodeIsFailure && forceCheckoutResult.Errors.IndexOf("unable to read tree") > 0)
            {
                // It is possible to have the above TryDownloadCommit() fail because we
                // already have the commit and root tree we intend to check out, but
                // don't have a tree further down the working directory. If we fail
                // checkout here, its' because we don't have these trees and the
                // read-object hook is not available yet. Force downloading the commit
                // again and retry the checkout.

                if (!this.TryDownloadCommit(
                    refs.GetTipCommitId(branch),
                    enlistment,
                    objectRequestor,
                    gitObjects,
                    gitRepo,
                    out errorMessage,
                    checkLocalObjectCache: false))
                {
                    return new Result(errorMessage);
                }

                forceCheckoutResult = git.ForceCheckout(branch);
            }

            if (forceCheckoutResult.ExitCodeIsFailure)
            {
                string[] errorLines = forceCheckoutResult.Errors.Split('\n');
                StringBuilder checkoutErrors = new StringBuilder();
                foreach (string gitError in errorLines)
                {
                    if (IsForceCheckoutErrorCloneFailure(gitError))
                    {
                        checkoutErrors.AppendLine(gitError);
                    }
                }

                if (checkoutErrors.Length > 0)
                {
                    string error = "Could not complete checkout of branch: " + branch + ", " + checkoutErrors.ToString();
                    tracer.RelatedError(error);
                    return new Result(error);
                }
            }

            if (!RepoMetadata.TryInitialize(tracer, enlistment.DotGVFSRoot, out errorMessage))
            {
                tracer.RelatedError(errorMessage);
                return new Result(errorMessage);
            }

            try
            {
                RepoMetadata.Instance.SaveCloneMetadata(tracer, enlistment);
                this.LogEnlistmentInfoAndSetConfigValues(tracer, git, enlistment);
            }
            catch (Exception e)
            {
                tracer.RelatedError(e.ToString());
                return new Result(e.Message);
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            // Prepare the working directory folder for GVFS last to ensure that gvfs mount will fail if gvfs clone has failed
            Exception exception;
            string prepFileSystemError;
            if (!GVFSPlatform.Instance.KernelDriver.TryPrepareFolderForCallbacks(enlistment.WorkingDirectoryBackingRoot, out prepFileSystemError, out exception))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(prepFileSystemError), prepFileSystemError);
                if (exception != null)
                {
                    metadata.Add("Exception", exception.ToString());
                }

                tracer.RelatedError(metadata, $"{nameof(this.CreateClone)}: TryPrepareFolderForCallbacks failed");
                return new Result(prepFileSystemError);
            }

            return new Result(true);
        }

        // TODO(#1364): Don't call this method on POSIX platforms (or have it no-op on them)
        private void CreateGitScript(GVFSEnlistment enlistment)
        {
            FileInfo gitCmd = new FileInfo(Path.Combine(enlistment.EnlistmentRoot, "git.cmd"));
            using (FileStream fs = gitCmd.Create())
            using (StreamWriter writer = new StreamWriter(fs))
            {
                writer.Write(
@"
@echo OFF
echo .
echo ^[105;30m
echo      This repo was cloned using GVFS, and the git repo is in the 'src' directory
echo      Switching you to the 'src' directory and rerunning your git command
echo                                                                                      [0m

@echo ON
cd src
git %*
");
            }

            gitCmd.Attributes = FileAttributes.Hidden;
        }

        private Result TryInitRepo(ITracer tracer, GitRefs refs, Enlistment enlistmentToInit)
        {
            string repoPath = enlistmentToInit.WorkingDirectoryBackingRoot;
            GitProcess.Result initResult = GitProcess.Init(enlistmentToInit);
            if (initResult.ExitCodeIsFailure)
            {
                string error = string.Format("Could not init repo at to {0}: {1}", repoPath, initResult.Errors);
                tracer.RelatedError(error);
                return new Result(error);
            }

            try
            {
                GVFSPlatform.Instance.FileSystem.EnsureDirectoryIsOwnedByCurrentUser(enlistmentToInit.DotGitRoot);
            }
            catch (IOException e)
            {
                string error = string.Format("Could not ensure .git directory is owned by current user: {0}", e.Message);
                tracer.RelatedError(error);
                return new Result(error);
            }

            GitProcess.Result remoteAddResult = new GitProcess(enlistmentToInit).RemoteAdd("origin", enlistmentToInit.RepoUrl);
            if (remoteAddResult.ExitCodeIsFailure)
            {
                string error = string.Format("Could not add remote to {0}: {1}", repoPath, remoteAddResult.Errors);
                tracer.RelatedError(error);
                return new Result(error);
            }

            File.WriteAllText(
                Path.Combine(repoPath, GVFSConstants.DotGit.PackedRefs),
                refs.ToPackedRefs());

            return new Result(true);
        }

        private class Result
        {
            public Result(bool success)
            {
                this.Success = success;
                this.ErrorMessage = string.Empty;
            }

            public Result(string errorMessage)
            {
                this.Success = false;
                this.ErrorMessage = errorMessage;
            }

            public bool Success { get; }
            public string ErrorMessage { get; }
        }
    }
}
