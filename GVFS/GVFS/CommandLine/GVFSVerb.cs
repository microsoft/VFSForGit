using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace GVFS.CommandLine
{
    public abstract class GVFSVerb
    {
        protected const string StartServiceInstructions = "Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";

        private readonly bool validateOriginURL;

        public GVFSVerb(bool validateOrigin = true)
        {
            this.Output = Console.Out;

            // Currently stderr is only being used for machine readable output for failures in sparse --prune
            this.ErrorOutput = Console.Error;
            this.ReturnCode = ReturnCode.Success;
            this.validateOriginURL = validateOrigin;
            this.ServiceName = GVFSConstants.Service.ServiceName;
            this.StartedByService = false;
            this.Unattended = GVFSEnlistment.IsUnattended(tracer: null);

            this.InitializeDefaultParameterValues();
        }

        [Flags]
        private enum GitCoreGVFSFlags
        {
            // GVFS_SKIP_SHA_ON_INDEX
            // Disables the calculation of the sha when writing the index
            SkipShaOnIndex = 1 << 0,

            // GVFS_BLOCK_COMMANDS
            // Blocks git commands that are not allowed in a GVFS/Scalar repo
            BlockCommands = 1 << 1,

            // GVFS_MISSING_OK
            // Normally git write-tree ensures that the objects referenced by the
            // directory exist in the object database.This option disables this check.
            MissingOk = 1 << 2,

            // GVFS_NO_DELETE_OUTSIDE_SPARSECHECKOUT
            // When marking entries to remove from the index and the working
            // directory this option will take into account what the
            // skip-worktree bit was set to so that if the entry has the
            // skip-worktree bit set it will not be removed from the working
            // directory.  This will allow virtualized working directories to
            // detect the change to HEAD and use the new commit tree to show
            // the files that are in the working directory.
            NoDeleteOutsideSparseCheckout = 1 << 3,

            // GVFS_FETCH_SKIP_REACHABILITY_AND_UPLOADPACK
            // While performing a fetch with a virtual file system we know
            // that there will be missing objects and we don't want to download
            // them just because of the reachability of the commits.  We also
            // don't want to download a pack file with commits, trees, and blobs
            // since these will be downloaded on demand.  This flag will skip the
            // checks on the reachability of objects during a fetch as well as
            // the upload pack so that extraneous objects don't get downloaded.
            FetchSkipReachabilityAndUploadPack = 1 << 4,

            // 1 << 5 has been deprecated

            // GVFS_BLOCK_FILTERS_AND_EOL_CONVERSIONS
            // With a virtual file system we only know the file size before any
            // CRLF or smudge/clean filters processing is done on the client.
            // To prevent file corruption due to truncation or expansion with
            // garbage at the end, these filters must not run when the file
            // is first accessed and brought down to the client. Git.exe can't
            // currently tell the first access vs subsequent accesses so this
            // flag just blocks them from occurring at all.
            BlockFiltersAndEolConversions = 1 << 6,

            // GVFS_PREFETCH_DURING_FETCH
            // While performing a `git fetch` command, use the gvfs-helper to
            // perform a "prefetch" of commits and trees.
            PrefetchDuringFetch = 1 << 7,
        }

        public abstract string EnlistmentRootPathParameter { get; set; }

        [Option(
            GVFSConstants.VerbParameters.InternalUseOnly,
            Required = false,
            HelpText = "This parameter is reserved for internal use.")]
        public string InternalParameters
        {
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        InternalVerbParameters mountInternal = InternalVerbParameters.FromJson(value);
                        if (!string.IsNullOrEmpty(mountInternal.ServiceName))
                        {
                            this.ServiceName = mountInternal.ServiceName;
                        }

                        if (!string.IsNullOrEmpty(mountInternal.MaintenanceJob))
                        {
                            this.MaintenanceJob = mountInternal.MaintenanceJob;
                        }

                        if (!string.IsNullOrEmpty(mountInternal.PackfileMaintenanceBatchSize))
                        {
                            this.PackfileMaintenanceBatchSize = mountInternal.PackfileMaintenanceBatchSize;
                        }

                        this.StartedByService = mountInternal.StartedByService;
                    }
                    catch (JsonReaderException e)
                    {
                        this.ReportErrorAndExit("Failed to parse InternalParameters: {0}.\n {1}", value, e);
                    }
                }
            }
        }

        public string ServiceName { get; set; }

        public string MaintenanceJob { get; set; }

        public string PackfileMaintenanceBatchSize { get; set; }

        public bool StartedByService { get; set; }

        public bool Unattended { get; private set; }

        public string ServicePipeName
        {
            get
            {
                return GVFSPlatform.Instance.GetGVFSServiceNamedPipeName(this.ServiceName);
            }
        }

        public TextWriter Output { get; set; }
        public TextWriter ErrorOutput { get; set; }

        public ReturnCode ReturnCode { get; private set; }

        protected abstract string VerbName { get; }

        public static bool TrySetRequiredGitConfigSettings(Enlistment enlistment)
        {
            string expectedHooksPath = Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.Root);
            expectedHooksPath = Paths.ConvertPathToGitFormat(expectedHooksPath);

            string gitStatusCachePath = null;
            if (!GVFSEnlistment.IsUnattended(tracer: null) && GVFSPlatform.Instance.IsGitStatusCacheSupported())
            {
                gitStatusCachePath = Path.Combine(
                    enlistment.EnlistmentRoot,
                    GVFSPlatform.Instance.Constants.DotGVFSRoot,
                    GVFSConstants.DotGVFS.GitStatusCache.CachePath);

                gitStatusCachePath = Paths.ConvertPathToGitFormat(gitStatusCachePath);
            }

            string coreGVFSFlags = Convert.ToInt32(
                GitCoreGVFSFlags.SkipShaOnIndex |
                GitCoreGVFSFlags.BlockCommands |
                GitCoreGVFSFlags.MissingOk |
                GitCoreGVFSFlags.NoDeleteOutsideSparseCheckout |
                GitCoreGVFSFlags.FetchSkipReachabilityAndUploadPack |
                GitCoreGVFSFlags.BlockFiltersAndEolConversions)
                .ToString();

            // These settings are required for normal GVFS functionality.
            // They will override any existing local configuration values.
            //
            // IMPORTANT! These must parallel the settings in ControlGitRepo:Initialize
            //
            Dictionary<string, string> requiredSettings = new Dictionary<string, string>
            {
                 // When running 'git am' it will remove the CRs from the patch file by default. This causes the patch to fail to apply because the
                 // file that is getting the patch applied will still have the CRs. There is a --keep-cr option that you can pass the 'git am' command
                 // but since we always want to keep CRs it is better to just set the config setting to always keep them so the user doesn't have to
                 // remember to pass the flag.
                { "am.keepcr", "true" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'checkout.optimizeNewBranch=true' to enable optimized 'checkout -b'
                { "checkout.optimizenewbranch", "true" },

                // We don't support line ending conversions - automatic conversion of LF to Crlf by git would cause un-necessary hydration. Disabling it.
                { "core.autocrlf", "false" },

                // Enable commit graph. https://devblogs.microsoft.com/devops/supercharging-the-git-commit-graph/
                { "core.commitGraph", "true" },

                // Perf - Git for Windows uses this to bulk-read and cache lstat data of entire directories (instead of doing lstat file by file).
                { "core.fscache", "true" },

                // Turns on all special gvfs logic. https://github.com/microsoft/git/blob/be5e0bb969495c428e219091e6976b52fb33b301/gvfs.h
                { "core.gvfs", coreGVFSFlags },

                // Use 'multi-pack-index' builtin instead of 'midx' to match upstream implementation
                { "core.multiPackIndex", "true" },

                // Perf - Enable parallel index preload for operations like git diff
                { "core.preloadIndex", "true" },

                // VFS4G never wants git to adjust line endings (causes un-necessary hydration of files)- explicitly setting core.safecrlf to false.
                { "core.safecrlf", "false" },

                // Possibly cause hydration while creating untrackedCache.
                { "core.untrackedCache", "false" },

                // This is to match what git init does.
                { "core.repositoryformatversion", "0" },

                // Turn on support for file modes on Mac & Linux.
                { "core.filemode", GVFSPlatform.Instance.FileSystem.SupportsFileMode ? "true" : "false" },

                // For consistency with git init.
                { "core.bare", "false" },

                // For consistency with git init.
                { "core.logallrefupdates", "true" },

                // Git to download objects on demand.
                { GitConfigSetting.CoreVirtualizeObjectsName, "true" },

                // Configure hook that git calls to get the paths git needs to consider for changes or untracked files
                { GitConfigSetting.CoreVirtualFileSystemName, Paths.ConvertPathToGitFormat(GVFSConstants.DotGit.Hooks.VirtualFileSystemPath) },

                // Ensure hooks path is configured correctly.
                { "core.hookspath", expectedHooksPath },

                // Hostname is no longer sufficent for VSTS authentication. VSTS now requires dev.azure.com/account to determine the tenant.
                // By setting useHttpPath, credential managers will get the path which contains the account as the first parameter. They can then use this information for auth appropriately.
                { GitConfigSetting.CredentialUseHttpPath, "true" },

                // Turn off credential validation(https://github.com/microsoft/Git-Credential-Manager-for-Windows/blob/master/Docs/Configuration.md#validate).
                // We already have logic to call git credential if we get back a 401, so there's no need to validate the PAT each time we ask for it.
                { "credential.validate", "false" },

                // This setting is not needed anymore, because current version of gvfs does not use index.lock.
                // (This change was introduced initially to prevent `git diff` from acquiring index.lock file.)
                // Explicitly setting this to true (which also is the default value) because the repo could have been
                // cloned in the past when autoRefreshIndex used to be set to false.
                { "diff.autoRefreshIndex", "true" },

                // In Git 2.24.0, some new config settings were created. Disable them locally in VFS for Git repos in case a user has set them globally.
                // https://github.com/microsoft/VFSForGit/pull/1594
                // This applies to feature.manyFiles, feature.experimental and fetch.writeCommitGraph settings.
                { "feature.manyFiles", "false" },
                { "feature.experimental", "false" },
                { "fetch.writeCommitGraph", "false" },

                // Turn off of git garbage collection. Git garbage collection does not work with virtualized object.
                // We do run maintenance jobs now that do the packing of loose objects so in theory we shouldn't need
                // this - but it is not hurting anything and it will prevent a gc from getting kicked off if for some
                // reason the maintenance jobs have not been running and there are too many loose objects
                { "gc.auto", "0" },

                // Prevent git GUI from displaying GC warnings.
                { "gui.gcwarning", "false" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'index.threads=true' to enable multi-threaded index reads
                { "index.threads", "true" },

                // index parsing code in VFSForGit currently only supports version 4.
                { "index.version", "4" },

                // Perf - avoid un-necessary blob downloads during a merge.
                { "merge.stat", "false" },

                // Perf - avoid un-necessary blob downloads while git tries to search and find renamed files.
                { "merge.renames", "false" },

                // Don't use bitmaps to determine pack file contents, because we use MIDX for this.
                { "pack.useBitmaps", "false" },

                // Update Git to include sparse push algorithm
                { "pack.useSparse", "true" },

                // Stop automatic git GC
                { "receive.autogc", "false" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'reset.quiet=true' to speed up 'git reset <foo>"
                { "reset.quiet", "true" },

                // Configure git to use our serialize status file - make git use the serialized status file rather than compute the status by
                // parsing the index file and going through the files to determine changes.
                { "status.deserializePath", gitStatusCachePath },

                // The GVFS Protocol forbids submodules, so prevent a user's
                // global config of "status.submoduleSummary=true" from causing
                // extreme slowness in "git status"
                { "status.submoduleSummary", "false" },

                // Generation number v2 isn't ready for full use. Wait for v3.
                { "commitGraph.generationVersion", "1" },
            };

            if (!TrySetConfig(enlistment, requiredSettings, isRequired: true))
            {
                return false;
            }

            return true;
        }

        public static bool TrySetOptionalGitConfigSettings(Enlistment enlistment)
        {
            // These settings are optional, because they impact performance but not functionality of GVFS.
            // These settings should only be set by the clone or repair verbs, so that they do not
            // overwrite the values set by the user in their local config.
            Dictionary<string, string> optionalSettings = new Dictionary<string, string>
            {
                { "status.aheadbehind", "false" },
            };

            if (!TrySetConfig(enlistment, optionalSettings, isRequired: false))
            {
                return false;
            }

            return true;
        }

        public abstract void Execute();

        public virtual void InitializeDefaultParameterValues()
        {
        }

        protected ReturnCode Execute<TVerb>(
            string enlistmentRootPath,
            Action<TVerb> configureVerb = null)
            where TVerb : GVFSVerb, new()
        {
            TVerb verb = new TVerb();
            verb.EnlistmentRootPathParameter = enlistmentRootPath;
            verb.ServiceName = this.ServiceName;
            verb.Unattended = this.Unattended;

            if (configureVerb != null)
            {
                configureVerb(verb);
            }

            try
            {
                verb.Execute();
            }
            catch (VerbAbortedException)
            {
            }

            return verb.ReturnCode;
        }

        protected ReturnCode Execute<TVerb>(
            GVFSEnlistment enlistment,
            Action<TVerb> configureVerb = null)
            where TVerb : GVFSVerb.ForExistingEnlistment, new()
        {
            TVerb verb = new TVerb();
            verb.EnlistmentRootPathParameter = enlistment.EnlistmentRoot;
            verb.ServiceName = this.ServiceName;
            verb.Unattended = this.Unattended;

            if (configureVerb != null)
            {
                configureVerb(verb);
            }

            try
            {
                verb.Execute(enlistment.Authentication);
            }
            catch (VerbAbortedException)
            {
            }

            return verb.ReturnCode;
        }

        protected bool ShowStatusWhileRunning(
            Func<bool> action,
            string message,
            string gvfsLogEnlistmentRoot)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                action,
                message,
                this.Output,
                showSpinner: !this.Unattended && this.Output == Console.Out && !GVFSPlatform.Instance.IsConsoleOutputRedirectedToFile(),
                gvfsLogEnlistmentRoot: gvfsLogEnlistmentRoot,
                initialDelayMs: 0);
        }

        protected bool ShowStatusWhileRunning(
            Func<bool> action,
            string message,
            bool suppressGvfsLogMessage = false)
        {
            string gvfsLogEnlistmentRoot = null;
            if (!suppressGvfsLogMessage)
            {
                string errorMessage;
                GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(this.EnlistmentRootPathParameter, out gvfsLogEnlistmentRoot, out errorMessage);
            }

            return this.ShowStatusWhileRunning(action, message, gvfsLogEnlistmentRoot);
        }

        protected bool TryAuthenticate(ITracer tracer, GVFSEnlistment enlistment, out string authErrorMessage)
        {
            string authError = null;

            bool result = this.ShowStatusWhileRunning(
                () => enlistment.Authentication.TryInitialize(tracer, enlistment, out authError),
                "Authenticating",
                enlistment.EnlistmentRoot);

            authErrorMessage = authError;
            return result;
        }

        protected void ReportErrorAndExit(ITracer tracer, ReturnCode exitCode, string error, params object[] args)
        {
            if (!string.IsNullOrEmpty(error))
            {
                if (args == null || args.Length == 0)
                {
                    this.Output.WriteLine(error);
                    if (tracer != null && exitCode != ReturnCode.Success)
                    {
                        tracer.RelatedError(error);
                    }
                }
                else
                {
                    this.Output.WriteLine(error, args);
                    if (tracer != null && exitCode != ReturnCode.Success)
                    {
                        tracer.RelatedError(error, args);
                    }
                }
            }

            this.ReturnCode = exitCode;
            throw new VerbAbortedException(this);
        }

        protected void ReportErrorAndExit(string error, params object[] args)
        {
            this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.GenericError, error: error, args: args);
        }

        protected void ReportErrorAndExit(ITracer tracer, string error, params object[] args)
        {
            this.ReportErrorAndExit(tracer, ReturnCode.GenericError, error, args);
        }

        protected RetryConfig GetRetryConfig(ITracer tracer, GVFSEnlistment enlistment, TimeSpan? timeoutOverride = null)
        {
            RetryConfig retryConfig;
            string error;
            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
            {
                this.ReportErrorAndExit(tracer, "Failed to determine GVFS timeout and max retries: " + error);
            }

            if (timeoutOverride.HasValue)
            {
                retryConfig.Timeout = timeoutOverride.Value;
            }

            return retryConfig;
        }

        protected ServerGVFSConfig QueryGVFSConfig(ITracer tracer, GVFSEnlistment enlistment, RetryConfig retryConfig)
        {
            ServerGVFSConfig serverGVFSConfig = null;
            string errorMessage = null;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                    {
                        const bool LogErrors = true;
                        return configRequestor.TryQueryGVFSConfig(LogErrors, out serverGVFSConfig, out _, out errorMessage);
                    }
                },
                "Querying remote for config",
                suppressGvfsLogMessage: true))
            {
                this.ReportErrorAndExit(tracer, "Unable to query /gvfs/config" + Environment.NewLine + errorMessage);
            }

            return serverGVFSConfig;
        }

        protected bool IsExistingPipeListening(string enlistmentRoot)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot)))
            {
                if (pipeClient.Connect(500))
                {
                    return true;
                }
            }

            return false;
        }

        protected void ValidateClientVersions(ITracer tracer, GVFSEnlistment enlistment, ServerGVFSConfig gvfsConfig, bool showWarnings)
        {
            this.CheckGitVersion(tracer, enlistment, out string gitVersion);
            enlistment.SetGitVersion(gitVersion);

            this.CheckGVFSHooksVersion(tracer, out string hooksVersion);
            enlistment.SetGVFSHooksVersion(hooksVersion);
            this.CheckFileSystemSupportsRequiredFeatures(tracer, enlistment);

            string errorMessage = null;
            bool errorIsFatal = false;
            if (!this.TryValidateGVFSVersion(enlistment, tracer, gvfsConfig, out errorMessage, out errorIsFatal))
            {
                if (errorIsFatal)
                {
                    this.ReportErrorAndExit(tracer, errorMessage);
                }
                else if (showWarnings)
                {
                    this.Output.WriteLine();
                    this.Output.WriteLine(errorMessage);
                    this.Output.WriteLine();
                }
            }
        }

        protected bool TryCreateAlternatesFile(PhysicalFileSystem fileSystem, GVFSEnlistment enlistment, out string errorMessage)
        {
            try
            {
                string alternatesFilePath = this.GetAlternatesPath(enlistment);
                string tempFilePath = alternatesFilePath + ".tmp";
                fileSystem.WriteAllText(tempFilePath, enlistment.GitObjectsRoot);
                fileSystem.MoveAndOverwriteFile(tempFilePath, alternatesFilePath);
            }
            catch (SecurityException e)
            {
                errorMessage = e.Message;
                return false;
            }
            catch (IOException e)
            {
                errorMessage = e.Message;
                return false;
            }

            errorMessage = null;
            return true;
        }

        protected void CheckGVFSHooksVersion(ITracer tracer, out string hooksVersion)
        {
            string error;
            if (!GVFSPlatform.Instance.TryGetGVFSHooksVersion(out hooksVersion, out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            string gvfsVersion = ProcessHelper.GetCurrentProcessVersion();
            if (hooksVersion != gvfsVersion)
            {
                this.ReportErrorAndExit(tracer, "GVFS.Hooks version ({0}) does not match GVFS version ({1}).", hooksVersion, gvfsVersion);
            }
        }

        protected void BlockEmptyCacheServerUrl(string userInput)
        {
            if (userInput == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                this.ReportErrorAndExit(
@"You must specify a value for the cache server.
You can specify a URL, a name of a configured cache server, or the special names None or Default.");
            }
        }

        protected CacheServerInfo ResolveCacheServer(
            ITracer tracer,
            CacheServerInfo cacheServer,
            CacheServerResolver cacheServerResolver,
            ServerGVFSConfig serverGVFSConfig)
        {
            CacheServerInfo resolvedCacheServer = cacheServer;

            if (cacheServer.Url == null)
            {
                string cacheServerName = cacheServer.Name;
                string error = null;

                if (!cacheServerResolver.TryResolveUrlFromRemote(
                        cacheServerName,
                        serverGVFSConfig,
                        out resolvedCacheServer,
                        out error))
                {
                    this.ReportErrorAndExit(tracer, error);
                }
            }
            else if (cacheServer.Name.Equals(CacheServerInfo.ReservedNames.UserDefined))
            {
                resolvedCacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, serverGVFSConfig);
            }

            this.Output.WriteLine("Using cache server: " + resolvedCacheServer);
            return resolvedCacheServer;
        }

        protected void ValidatePathParameter(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    Path.GetFullPath(path);
                }
                catch (Exception e)
                {
                    this.ReportErrorAndExit("Invalid path: '{0}' ({1})", path, e.Message);
                }
            }
        }

        protected bool TryDownloadCommit(
            string commitId,
            GVFSEnlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            GVFSGitObjects gitObjects,
            GitRepo repo,
            out string error,
            bool checkLocalObjectCache = true)
        {
            if (!checkLocalObjectCache || !repo.CommitAndRootTreeExists(commitId))
            {
                if (!gitObjects.TryDownloadCommit(commitId))
                {
                    error = "Could not download commit " + commitId + " from: " + Uri.EscapeUriString(objectRequestor.CacheServer.ObjectsEndpointUrl);
                    return false;
                }
            }

            error = null;
            return true;
        }

        protected bool TryDownloadRootGitAttributes(GVFSEnlistment enlistment, GVFSGitObjects gitObjects, GitRepo repo, out string error)
        {
            List<DiffTreeResult> rootEntries = new List<DiffTreeResult>();
            GitProcess git = new GitProcess(enlistment);
            GitProcess.Result result = git.LsTree(
                GVFSConstants.DotGit.HeadName,
                line => rootEntries.Add(DiffTreeResult.ParseFromLsTreeLine(line)),
                recursive: false);

            if (result.ExitCodeIsFailure)
            {
                error = "Error returned from ls-tree to find " + GVFSConstants.SpecialGitFiles.GitAttributes + " file: " + result.Errors;
                return false;
            }

            DiffTreeResult gitAttributes = rootEntries.FirstOrDefault(entry => entry.TargetPath.Equals(GVFSConstants.SpecialGitFiles.GitAttributes));
            if (gitAttributes == null)
            {
                error = "This branch does not contain a " + GVFSConstants.SpecialGitFiles.GitAttributes + " file in the root folder.  This file is required by GVFS clone";
                return false;
            }

            if (!repo.ObjectExists(gitAttributes.TargetSha))
            {
                if (gitObjects.TryDownloadAndSaveObject(gitAttributes.TargetSha, GVFSGitObjects.RequestSource.GVFSVerb) != GitObjects.DownloadAndSaveObjectResult.Success)
                {
                    error = "Could not download " + GVFSConstants.SpecialGitFiles.GitAttributes + " file";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Request that PrjFlt be enabled and attached to the volume of the enlistment root
        /// </summary>
        /// <param name="enlistmentRoot">Enlistment root.  If string.Empty, PrjFlt will be enabled but not attached to any volumes</param>
        /// <param name="errorMessage">Error meesage (in the case of failure)</param>
        /// <returns>True is successful and false otherwise</returns>
        protected bool TryEnableAndAttachPrjFltThroughService(string enlistmentRoot, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.EnableAndAttachProjFSRequest request = new NamedPipeMessages.EnableAndAttachProjFSRequest();
            request.EnlistmentRoot = enlistmentRoot;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "GVFS.Service is not responding. " + GVFSVerb.StartServiceInstructions;
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.EnableAndAttachProjFSRequest.Response.Header)
                    {
                        NamedPipeMessages.EnableAndAttachProjFSRequest.Response message = NamedPipeMessages.EnableAndAttachProjFSRequest.Response.FromMessage(response);

                        if (!string.IsNullOrEmpty(message.ErrorMessage))
                        {
                            errorMessage = message.ErrorMessage;
                            return false;
                        }

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = $"Failed to attach ProjFS to volume.";
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("GVFS.Service responded with unexpected message: {0}", response);
                        return false;
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with GVFS.Service: " + e.ToString();
                    return false;
                }
            }
        }

        protected void LogEnlistmentInfoAndSetConfigValues(ITracer tracer, GitProcess git, GVFSEnlistment enlistment)
        {
            string mountId = CreateMountId();
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);
            metadata.Add(nameof(mountId), mountId);
            metadata.Add("Enlistment", enlistment);
            metadata.Add("PhysicalDiskInfo", GVFSPlatform.Instance.GetPhysicalDiskInfo(enlistment.WorkingDirectoryRoot, sizeStatsOnly: false));
            tracer.RelatedEvent(EventLevel.Informational, "EnlistmentInfo", metadata, Keywords.Telemetry);

            GitProcess.Result configResult = git.SetInLocalConfig(GVFSConstants.GitConfig.EnlistmentId, RepoMetadata.Instance.EnlistmentId, replaceAll: true);
            if (configResult.ExitCodeIsFailure)
            {
                string error = "Could not update config with enlistment id, error: " + configResult.Errors;
                tracer.RelatedWarning(error);
            }

            configResult = git.SetInLocalConfig(GVFSConstants.GitConfig.MountId, mountId, replaceAll: true);
            if (configResult.ExitCodeIsFailure)
            {
                string error = "Could not update config with mount id, error: " + configResult.Errors;
                tracer.RelatedWarning(error);
            }
        }

        private static string CreateMountId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static bool TrySetConfig(Enlistment enlistment, Dictionary<string, string> configSettings, bool isRequired)
        {
            GitProcess git = new GitProcess(enlistment);

            Dictionary<string, GitConfigSetting> existingConfigSettings;

            // If the settings are required, then only check local config settings, because we don't want to depend on
            // global settings that can then change independent of this repo.
            if (!git.TryGetAllConfig(localOnly: isRequired, configSettings: out existingConfigSettings))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> setting in configSettings)
            {
                GitConfigSetting existingSetting;
                if (setting.Value != null)
                {
                    if (!existingConfigSettings.TryGetValue(setting.Key, out existingSetting) ||
                        (isRequired && !existingSetting.HasValue(setting.Value)))
                    {
                        GitProcess.Result setConfigResult = git.SetInLocalConfig(setting.Key, setting.Value);
                        if (setConfigResult.ExitCodeIsFailure)
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (existingConfigSettings.TryGetValue(setting.Key, out existingSetting))
                    {
                        git.DeleteFromLocalConfig(setting.Key);
                    }
                }
            }

            return true;
        }

        private string GetAlternatesPath(GVFSEnlistment enlistment)
        {
            return Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Objects.Info.Alternates);
        }

        private void CheckFileSystemSupportsRequiredFeatures(ITracer tracer, Enlistment enlistment)
        {
            try
            {
                string warning;
                string error;
                if (!GVFSPlatform.Instance.KernelDriver.IsSupported(enlistment.EnlistmentRoot, out warning, out error))
                {
                    this.ReportErrorAndExit(tracer, $"Error: {error}");
                }
            }
            catch (VerbAbortedException)
            {
                // ReportErrorAndExit throws VerbAbortedException.  Catch and re-throw here so that GVFS does not report that
                // it failed to determine if file system supports required features
                throw;
            }
            catch (Exception e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Failed to determine if file system supports features required by GVFS");
                }

                this.ReportErrorAndExit(tracer, "Error: Failed to determine if file system supports features required by GVFS.");
            }
        }

        private void CheckGitVersion(ITracer tracer, GVFSEnlistment enlistment, out string version)
        {
            GitVersion gitVersion = null;
            if (string.IsNullOrEmpty(enlistment.GitBinPath) || !GitProcess.TryGetVersion(enlistment.GitBinPath, out gitVersion, out string _))
            {
                this.ReportErrorAndExit(tracer, "Error: Unable to retrieve the git version");
            }

            version = gitVersion.ToString();

            if (gitVersion.Platform != GVFSConstants.SupportedGitVersion.Platform)
            {
                this.ReportErrorAndExit(tracer, "Error: Invalid version of git {0}.  Must use gvfs version.", version);
            }

            if (gitVersion.IsLessThan(GVFSConstants.SupportedGitVersion))
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed git version {0} is less than the supported version of {1}.",
                    gitVersion,
                    GVFSConstants.SupportedGitVersion);
            }
            else if (gitVersion.Revision != GVFSConstants.SupportedGitVersion.Revision)
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed git version {0} has revision number {1} instead of {2}." +
                     " This Git version is too new, so either downgrade Git or upgrade VFS for Git",
                    gitVersion,
                    gitVersion.Revision,
                    GVFSConstants.SupportedGitVersion.Revision);
            }
        }

        private bool TryValidateGVFSVersion(GVFSEnlistment enlistment, ITracer tracer, ServerGVFSConfig config, out string errorMessage, out bool errorIsFatal)
        {
            errorMessage = null;
            errorIsFatal = false;

            using (ITracer activity = tracer.StartActivity("ValidateGVFSVersion", EventLevel.Informational))
            {
                Version currentVersion = new Version(ProcessHelper.GetCurrentProcessVersion());

                IEnumerable<ServerGVFSConfig.VersionRange> allowedGvfsClientVersions =
                    config != null
                    ? config.AllowedGVFSClientVersions
                    : null;

                if (allowedGvfsClientVersions == null || !allowedGvfsClientVersions.Any())
                {
                    errorMessage = "WARNING: Unable to validate your GVFS version" + Environment.NewLine;
                    if (config == null)
                    {
                        errorMessage += "Could not query valid GVFS versions from: " + Uri.EscapeUriString(enlistment.RepoUrl);
                    }
                    else
                    {
                        errorMessage += "Server not configured to provide supported GVFS versions";
                    }

                    EventMetadata metadata = new EventMetadata();
                    tracer.RelatedError(metadata, errorMessage, Keywords.Network);

                    return false;
                }

                foreach (ServerGVFSConfig.VersionRange versionRange in config.AllowedGVFSClientVersions)
                {
                    if (currentVersion >= versionRange.Min &&
                        (versionRange.Max == null || currentVersion <= versionRange.Max))
                    {
                        activity.RelatedEvent(
                            EventLevel.Informational,
                            "GVFSVersionValidated",
                            new EventMetadata
                            {
                                { "SupportedVersionRange", versionRange },
                            });

                        enlistment.SetGVFSVersion(currentVersion.ToString());
                        return true;
                    }
                }

                activity.RelatedError("GVFS version {0} is not supported", currentVersion);
            }

            errorMessage = "ERROR: Your GVFS version is no longer supported.  Install the latest and try again.";
            errorIsFatal = true;
            return false;
        }

        public abstract class ForExistingEnlistment : GVFSVerb
        {
            public ForExistingEnlistment(bool validateOrigin = true) : base(validateOrigin)
            {
            }

            [Value(
                0,
                Required = false,
                Default = "",
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the GVFS enlistment root")]
            public override string EnlistmentRootPathParameter { get; set; }

            public sealed override void Execute()
            {
                this.Execute(authentication: null);
            }

            public void Execute(GitAuthentication authentication)
            {
                this.ValidatePathParameter(this.EnlistmentRootPathParameter);

                this.PreCreateEnlistment();
                GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPathParameter, authentication);

                this.Execute(enlistment);
            }

            protected virtual void PreCreateEnlistment()
            {
            }

            protected abstract void Execute(GVFSEnlistment enlistment);

            protected void InitializeLocalCacheAndObjectsPaths(
                ITracer tracer,
                GVFSEnlistment enlistment,
                RetryConfig retryConfig,
                ServerGVFSConfig serverGVFSConfig,
                CacheServerInfo cacheServer)
            {
                string error;
                if (!RepoMetadata.TryInitialize(tracer, Path.Combine(enlistment.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot), out error))
                {
                    this.ReportErrorAndExit(tracer, "Failed to initialize repo metadata: " + error);
                }

                this.InitializeCachePathsFromRepoMetadata(tracer, enlistment);

                // Note: Repos cloned with a version of GVFS that predates the local cache will not have a local cache configured
                if (!string.IsNullOrWhiteSpace(enlistment.LocalCacheRoot))
                {
                    this.EnsureLocalCacheIsHealthy(tracer, enlistment, retryConfig, serverGVFSConfig, cacheServer);
                }

                RepoMetadata.Shutdown();
            }

            protected ReturnCode ExecuteGVFSVerb<TVerb>(ITracer tracer, Action<TVerb> configureVerb = null, TextWriter outputWriter = null)
                where TVerb : GVFSVerb, new()
            {
                try
                {
                    ReturnCode returnCode;
                    StringBuilder commandOutput = new StringBuilder();
                    using (StringWriter writer = new StringWriter(commandOutput))
                    {
                        returnCode = this.Execute<TVerb>(
                            this.EnlistmentRootPathParameter,
                            verb =>
                            {
                                verb.Output = outputWriter ?? writer;
                                configureVerb?.Invoke(verb);
                            });
                    }

                    EventMetadata metadata = new EventMetadata();
                    if (outputWriter == null)
                    {
                        metadata.Add("Output", commandOutput.ToString());
                    }
                    else
                    {
                        // If a parent verb is redirecting the output of its child, include a reminder
                        // that the child verb's activity was recorded in its own log file
                        metadata.Add("Output", $"Check {new TVerb().VerbName} logs for output");
                    }

                    metadata.Add("ReturnCode", returnCode);
                    tracer.RelatedEvent(EventLevel.Informational, typeof(TVerb).Name, metadata);

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

            protected void Unmount(ITracer tracer)
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

            private void InitializeCachePathsFromRepoMetadata(
                ITracer tracer,
                GVFSEnlistment enlistment)
            {
                string error;
                string gitObjectsRoot;
                if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
                {
                    this.ReportErrorAndExit(tracer, "Failed to determine git objects root from repo metadata: " + error);
                }

                if (string.IsNullOrWhiteSpace(gitObjectsRoot))
                {
                    this.ReportErrorAndExit(tracer, "Invalid git objects root (empty or whitespace)");
                }

                string localCacheRoot;
                if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
                {
                    this.ReportErrorAndExit(tracer, "Failed to determine local cache path from repo metadata: " + error);
                }

                // Note: localCacheRoot is allowed to be empty, this can occur when upgrading from disk layout version 11 to 12

                string blobSizesRoot;
                if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out blobSizesRoot, out error))
                {
                    this.ReportErrorAndExit(tracer, "Failed to determine blob sizes root from repo metadata: " + error);
                }

                if (string.IsNullOrWhiteSpace(blobSizesRoot))
                {
                    this.ReportErrorAndExit(tracer, "Invalid blob sizes root (empty or whitespace)");
                }

                enlistment.InitializeCachePaths(localCacheRoot, gitObjectsRoot, blobSizesRoot);
            }

            private void EnsureLocalCacheIsHealthy(
                ITracer tracer,
                GVFSEnlistment enlistment,
                RetryConfig retryConfig,
                ServerGVFSConfig serverGVFSConfig,
                CacheServerInfo cacheServer)
            {
                if (!Directory.Exists(enlistment.LocalCacheRoot))
                {
                    try
                    {
                        tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Local cache root: {enlistment.LocalCacheRoot} missing, recreating it");
                        Directory.CreateDirectory(enlistment.LocalCacheRoot);
                    }
                    catch (Exception e)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Exception", e.ToString());
                        metadata.Add("enlistment.LocalCacheRoot", enlistment.LocalCacheRoot);
                        tracer.RelatedError(metadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to create local cache root");

                        this.ReportErrorAndExit(tracer, "Failed to create local cache: " + enlistment.LocalCacheRoot);
                    }
                }

                // Validate that the GitObjectsRoot directory is on disk, and that the GVFS repo is configured to use it.
                // If the directory is missing (and cannot be found in the mapping file) a new key for the repo will be added
                // to the mapping file and used for BOTH the GitObjectsRoot and BlobSizesRoot
                PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                if (Directory.Exists(enlistment.GitObjectsRoot))
                {
                    bool gitObjectsRootInAlternates = false;

                    string alternatesFilePath = this.GetAlternatesPath(enlistment);
                    if (File.Exists(alternatesFilePath))
                    {
                        try
                        {
                            using (Stream stream = fileSystem.OpenFileStream(
                                alternatesFilePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.ReadWrite,
                                callFlushFileBuffers: false))
                            {
                                using (StreamReader reader = new StreamReader(stream))
                                {
                                    while (!reader.EndOfStream)
                                    {
                                        string alternatesLine = reader.ReadLine();
                                        if (string.Equals(alternatesLine, enlistment.GitObjectsRoot, GVFSPlatform.Instance.Constants.PathComparison))
                                        {
                                            gitObjectsRootInAlternates = true;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            EventMetadata exceptionMetadata = new EventMetadata();
                            exceptionMetadata.Add("Exception", e.ToString());
                            tracer.RelatedError(exceptionMetadata, $"{nameof(this.EnsureLocalCacheIsHealthy)}: Exception while trying to validate alternates file");

                            this.ReportErrorAndExit(tracer, $"Failed to validate that alternates file includes git objects root: {e.Message}");
                        }
                    }
                    else
                    {
                        tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Alternates file not found");
                    }

                    if (!gitObjectsRootInAlternates)
                    {
                        tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: GitObjectsRoot ({enlistment.GitObjectsRoot}) missing from alternates files, recreating alternates");
                        string error;
                        if (!this.TryCreateAlternatesFile(fileSystem, enlistment, out error))
                        {
                            this.ReportErrorAndExit(tracer, $"Failed to update alternates file to include git objects root: {error}");
                        }
                    }
                }
                else
                {
                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: GitObjectsRoot ({enlistment.GitObjectsRoot}) missing, determining new root");

                    if (cacheServer == null)
                    {
                        cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);
                    }

                    string error;
                    if (serverGVFSConfig == null)
                    {
                        if (retryConfig == null)
                        {
                            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                            {
                                this.ReportErrorAndExit(tracer, "Failed to determine GVFS timeout and max retries: " + error);
                            }
                        }

                        serverGVFSConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);
                    }

                    string localCacheKey;
                    LocalCacheResolver localCacheResolver = new LocalCacheResolver(enlistment);
                    if (!localCacheResolver.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers(
                        tracer,
                        serverGVFSConfig,
                        cacheServer,
                        enlistment.LocalCacheRoot,
                        localCacheKey: out localCacheKey,
                        errorMessage: out error))
                    {
                        this.ReportErrorAndExit(tracer, $"Previous git objects root ({enlistment.GitObjectsRoot}) not found, and failed to determine new local cache key: {error}");
                    }

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("localCacheRoot", enlistment.LocalCacheRoot);
                    metadata.Add("localCacheKey", localCacheKey);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "Initializing and persisting updated paths");
                    tracer.RelatedEvent(EventLevel.Informational, "GVFSVerb_EnsureLocalCacheIsHealthy_InitializePathsFromKey", metadata);
                    enlistment.InitializeCachePathsFromKey(enlistment.LocalCacheRoot, localCacheKey);

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating GitObjectsRoot ({enlistment.GitObjectsRoot}), GitPackRoot ({enlistment.GitPackRoot}), and BlobSizesRoot ({enlistment.BlobSizesRoot})");
                    try
                    {
                        Directory.CreateDirectory(enlistment.GitObjectsRoot);
                        Directory.CreateDirectory(enlistment.GitPackRoot);
                    }
                    catch (Exception e)
                    {
                        EventMetadata exceptionMetadata = new EventMetadata();
                        exceptionMetadata.Add("Exception", e.ToString());
                        exceptionMetadata.Add("enlistment.LocalCacheRoot", enlistment.LocalCacheRoot);
                        exceptionMetadata.Add("enlistment.GitObjectsRoot", enlistment.GitObjectsRoot);
                        exceptionMetadata.Add("enlistment.GitPackRoot", enlistment.GitPackRoot);
                        exceptionMetadata.Add("enlistment.BlobSizesRoot", enlistment.BlobSizesRoot);
                        tracer.RelatedError(exceptionMetadata, $"{nameof(this.InitializeLocalCacheAndObjectsPaths)}: Exception while trying to create objects, pack, and sizes folders");

                        this.ReportErrorAndExit(tracer, "Failed to create objects, pack, and sizes folders");
                    }

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating new alternates file");
                    if (!this.TryCreateAlternatesFile(fileSystem, enlistment, out error))
                    {
                        this.ReportErrorAndExit(tracer, $"Failed to update alterates file with new objects path: {error}");
                    }

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Saving git objects root ({enlistment.GitObjectsRoot}) in repo metadata");
                    RepoMetadata.Instance.SetGitObjectsRoot(enlistment.GitObjectsRoot);

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Saving blob sizes root ({enlistment.BlobSizesRoot}) in repo metadata");
                    RepoMetadata.Instance.SetBlobSizesRoot(enlistment.BlobSizesRoot);
                }

                // Validate that the BlobSizesRoot folder is on disk.
                // Note that if a user performed an action that resulted in the entire .gvfscache being deleted, the code above
                // for validating GitObjectsRoot will have already taken care of generating a new key and setting a new enlistment.BlobSizesRoot path
                if (!Directory.Exists(enlistment.BlobSizesRoot))
                {
                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: BlobSizesRoot ({enlistment.BlobSizesRoot}) not found, re-creating");
                    try
                    {
                        Directory.CreateDirectory(enlistment.BlobSizesRoot);
                    }
                    catch (Exception e)
                    {
                        EventMetadata exceptionMetadata = new EventMetadata();
                        exceptionMetadata.Add("Exception", e.ToString());
                        exceptionMetadata.Add("enlistment.BlobSizesRoot", enlistment.BlobSizesRoot);
                        tracer.RelatedError(exceptionMetadata, $"{nameof(this.InitializeLocalCacheAndObjectsPaths)}: Exception while trying to create blob sizes folder");

                        this.ReportErrorAndExit(tracer, "Failed to create blob sizes folder");
                    }
                }
            }

            private GVFSEnlistment CreateEnlistment(string enlistmentRootPath, GitAuthentication authentication)
            {
                string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
                if (string.IsNullOrWhiteSpace(gitBinPath))
                {
                    this.ReportErrorAndExit("Error: " + GVFSConstants.GitIsNotInstalledError);
                }

                GVFSEnlistment enlistment = null;
                try
                {
                    enlistment = GVFSEnlistment.CreateFromDirectory(
                        enlistmentRootPath,
                        gitBinPath,
                        authentication,
                        createWithoutRepoURL: !this.validateOriginURL);
                }
                catch (InvalidRepoException e)
                {
                    this.ReportErrorAndExit(
                        "Error: '{0}' is not a valid GVFS enlistment. {1}",
                        enlistmentRootPath,
                        e.Message);
                }

                return enlistment;
            }
        }

        public abstract class ForNoEnlistment : GVFSVerb
        {
            public ForNoEnlistment(bool validateOrigin = true) : base(validateOrigin)
            {
            }

            public override string EnlistmentRootPathParameter
            {
                get { throw new InvalidOperationException(); }
                set { throw new InvalidOperationException(); }
            }
        }

        public class VerbAbortedException : Exception
        {
            public VerbAbortedException(GVFSVerb verb)
            {
                this.Verb = verb;
            }

            public GVFSVerb Verb { get; }
        }
    }
}
