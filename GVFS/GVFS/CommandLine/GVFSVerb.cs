using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        public abstract string EnlistmentRootPathParameter { get; set; }

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
                    catch (JsonException e)
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
            Dictionary<string, string> requiredSettings = RequiredGitConfig.GetRequiredSettings(enlistment);

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
            return this.TryAuthenticateAndQueryGVFSConfig(tracer, enlistment, null, out _, out authErrorMessage);
        }

        /// <summary>
        /// Combines authentication and GVFS config query into a single operation,
        /// eliminating a redundant HTTP round-trip. If <paramref name="retryConfig"/>
        /// is null, a default RetryConfig is used.
        /// If the config query fails but a valid <paramref name="fallbackCacheServer"/>
        /// URL is available, auth succeeds but <paramref name="serverGVFSConfig"/>
        /// will be null (caller should handle this gracefully).
        /// </summary>
        protected bool TryAuthenticateAndQueryGVFSConfig(
            ITracer tracer,
            GVFSEnlistment enlistment,
            RetryConfig retryConfig,
            out ServerGVFSConfig serverGVFSConfig,
            out string errorMessage,
            CacheServerInfo fallbackCacheServer = null)
        {
            ServerGVFSConfig config = null;
            string error = null;

            bool result = this.ShowStatusWhileRunning(
                () => enlistment.Authentication.TryInitializeAndQueryGVFSConfig(
                    tracer,
                    enlistment,
                    retryConfig ?? new RetryConfig(),
                    out config,
                    out error),
                "Authenticating",
                enlistment.EnlistmentRoot);

            if (!result && fallbackCacheServer != null && !string.IsNullOrWhiteSpace(fallbackCacheServer.Url))
            {
                // Auth/config query failed, but we have a fallback cache server.
                // Allow auth to succeed so mount/clone can proceed; config will be null.
                tracer.RelatedWarning("Config query failed but continuing with fallback cache server: " + error);
                serverGVFSConfig = null;
                errorMessage = null;
                return true;
            }

            serverGVFSConfig = config;
            errorMessage = error;
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

        // QueryGVFSConfig for callers that require config to succeed (no fallback)
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
            if (!checkLocalObjectCache || !repo.CommitAndRootTreeExists(commitId, out _))
            {
                if (!gitObjects.TryDownloadCommit(commitId))
                {
                    error = "Could not download commit " + commitId + " from: " + Uri.EscapeDataString(objectRequestor.CacheServer.ObjectsEndpointUrl);
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
            // Use DotGitRoot (shared .git dir for worktrees) since
            // objects/info/alternates lives in the shared git directory.
            return Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.Objects.Info.AlternatesRelativePath);
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
                this.ReportErrorAndExit(tracer, "Error: Unable to retrieve the Git version");
            }

            version = gitVersion.ToString();

            if (gitVersion.Platform != GVFSConstants.SupportedGitVersion.Platform)
            {
                this.ReportErrorAndExit(tracer, "Error: Invalid version of Git {0}. Must use vfs version.", version);
            }

            if (gitVersion.IsLessThan(GVFSConstants.SupportedGitVersion))
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed Git version {0} is less than the minimum supported version of {1}.",
                    gitVersion,
                    GVFSConstants.SupportedGitVersion);
            }
            /* We require that the revision (Z) of the Git version string (2.X.Y.vfs.Z.W)
             * is an exact match. We will use this to signal that a microsoft/git version introduces
             * a breaking change that requires a VFS for Git upgrade.
             * Using the revision part allows us to modify the other version items arbitrarily,
             * including taking version numbers 2.X.Y from upstream and updating .W if we have any
             * hotfixes to microsoft/git.
             */
            else if (gitVersion.Revision != GVFSConstants.SupportedGitVersion.Revision)
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed Git version {0} has revision number {1} instead of {2}." +
                     " This Git version is too new, so either downgrade Git or upgrade VFS for Git." +
                     " The minimum supported version of Git is {3}.",
                    gitVersion,
                    gitVersion.Revision,
                    GVFSConstants.SupportedGitVersion.Revision,
                    GVFSConstants.SupportedGitVersion);
            }
        }

        private bool TryValidateGVFSVersion(GVFSEnlistment enlistment, ITracer tracer, ServerGVFSConfig config, out string errorMessage, out bool errorIsFatal)
        {
            errorMessage = null;
            errorIsFatal = false;

            using (ITracer activity = tracer.StartActivity("ValidateGVFSVersion", EventLevel.Informational))
            {
                if (ProcessHelper.IsDevelopmentVersion())
                {
                    /* Development Version will start with 0 and include a "+{commitID}" suffix
                     * so it won't ever be valid, but it needs to be able to run so we can test it. */
                    return true;
                }

                string recordedVersion = ProcessHelper.GetCurrentProcessVersion();
                // Work around the default behavior in .NET SDK 8 where the revision ID
                // is appended after a '+' character, which cannot be parsed by `System.Version`.
                int plus = recordedVersion.IndexOf('+');
                Version currentVersion = new Version(plus < 0 ? recordedVersion : recordedVersion.Substring(0, plus));
                IEnumerable<ServerGVFSConfig.VersionRange> allowedGvfsClientVersions =
                    config != null
                    ? config.AllowedGVFSClientVersions
                    : null;

                if (allowedGvfsClientVersions == null || !allowedGvfsClientVersions.Any())
                {
                    errorMessage = "WARNING: Unable to validate your GVFS version" + Environment.NewLine;
                    if (config == null)
                    {
                        errorMessage += "Could not query valid GVFS versions from: " + Uri.EscapeDataString(enlistment.RepoUrl);
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

        internal static System.CommandLine.Option<string> CreateInternalParametersOption()
        {
            return new System.CommandLine.Option<string>("--internal_use_only") { Description = "This parameter is reserved for internal use." };
        }

        internal static System.CommandLine.Argument<string> CreateEnlistmentPathArgument(bool required = false)
        {
            System.CommandLine.Argument<string> arg = new System.CommandLine.Argument<string>("enlistment-root-path");
            arg.Description = "Full or relative path to the GVFS enlistment root";
            arg.Arity = required ? System.CommandLine.ArgumentArity.ExactlyOne : System.CommandLine.ArgumentArity.ZeroOrOne;
            if (!required)
            {
                arg.DefaultValueFactory = (_) => "";
            }

            return arg;
        }

        internal static void ApplyInternalParameters(GVFSVerb verb, System.CommandLine.ParseResult result, System.CommandLine.Option<string> internalOption)
        {
            string internalParams = result.GetValue(internalOption);
            if (!string.IsNullOrEmpty(internalParams))
            {
                verb.InternalParameters = internalParams;
            }
        }

        internal static void SetActionForVerbWithEnlistment<T>(
            System.CommandLine.Command cmd,
            System.CommandLine.Argument<string> enlistmentArg,
            System.CommandLine.Option<string> internalOption,
            bool defaultEnlistmentPathToCwd,
            Action<T, System.CommandLine.ParseResult> setVerbProperties = null) where T : GVFSVerb, new()
        {
            cmd.SetAction((System.CommandLine.ParseResult result) =>
            {
                T verb = new T();
                verb.EnlistmentRootPathParameter = result.GetValue(enlistmentArg) ?? "";
                if (verb.EnlistmentRootPathParameter.StartsWith("-"))
                {
                    Console.Error.WriteLine($"Unrecognized option '{verb.EnlistmentRootPathParameter}'");
                    Environment.Exit((int)ReturnCode.ParsingError);
                }

                if (defaultEnlistmentPathToCwd && string.IsNullOrEmpty(verb.EnlistmentRootPathParameter))
                {
                    verb.EnlistmentRootPathParameter = Environment.CurrentDirectory;
                }

                setVerbProperties?.Invoke(verb, result);
                ApplyInternalParameters(verb, result, internalOption);
                try
                {
                    verb.Execute();
                }
                catch (VerbAbortedException)
                {
                }

                Environment.Exit((int)verb.ReturnCode);
            });
        }

        internal static void SetActionForNoEnlistment<T>(
            System.CommandLine.Command cmd,
            System.CommandLine.Option<string> internalOption,
            Action<T, System.CommandLine.ParseResult> setVerbProperties = null) where T : ForNoEnlistment, new()
        {
            cmd.SetAction((System.CommandLine.ParseResult result) =>
            {
                T verb = new T();
                setVerbProperties?.Invoke(verb, result);
                ApplyInternalParameters(verb, result, internalOption);
                try
                {
                    verb.Execute();
                }
                catch (VerbAbortedException)
                {
                }

                Environment.Exit((int)verb.ReturnCode);
            });
        }

        public abstract class ForExistingEnlistment : GVFSVerb
        {
            public ForExistingEnlistment(bool validateOrigin = true) : base(validateOrigin)
            {
            }

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
