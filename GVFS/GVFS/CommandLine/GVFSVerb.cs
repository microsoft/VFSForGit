using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;

namespace GVFS.CommandLine
{
    public abstract class GVFSVerb
    {
        protected const string StartServiceInstructions = "Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";

        public GVFSVerb()
        {
            this.Output = Console.Out;
            this.ReturnCode = ReturnCode.Success;
            this.ServiceName = GVFSConstants.Service.ServiceName;

            this.Unattended = GVFSEnlistment.IsUnattended(tracer: null);

            this.InitializeDefaultParameterValues();
        }

        public abstract string EnlistmentRootPath { get; set; }

        [Option(
            GVFSConstants.VerbParameters.Mount.ServiceName,
            Default = GVFSConstants.Service.ServiceName,
            Required = false,
            HelpText = "This parameter is reserved for internal use.")]
        public string ServiceName { get; set; }

        public bool Unattended { get; private set; }

        public string ServicePipeName
        {
            get
            {
                return this.ServiceName + ".Pipe";
            }
        }

        public TextWriter Output { get; set; }

        public ReturnCode ReturnCode { get; private set; }

        protected abstract string VerbName { get; }

        public static bool TrySetGitConfigSettings(Enlistment enlistment)
        {
            string expectedHooksPath = Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.Root);
            expectedHooksPath = expectedHooksPath.Replace('\\', '/');

            Dictionary<string, string> expectedConfigSettings = new Dictionary<string, string>
            {
                { "am.keepcr", "true" },
                { "core.autocrlf", "false" },
                { "core.fscache", "true" },
                { "core.gvfs", "true" },
                { "core.preloadIndex", "true" },
                { "core.safecrlf", "false" },
                { "core.sparseCheckout", "true" },
                { "core.untrackedCache", "false" },
                { "core.repositoryformatversion", "0" },
                { "core.filemode", "false" },
                { "core.bare", "false" },
                { "core.logallrefupdates", "true" },
                { GitConfigSetting.VirtualizeObjectsGitConfigName, "true" },
                { "core.hookspath", expectedHooksPath },
                { "credential.validate", "false" },
                { "diff.autoRefreshIndex", "false" },
                { "gc.auto", "0" },
                { "gui.gcwarning", "false" },
                { "index.version", "4" },
                { "merge.stat", "false" },
                { "receive.autogc", "false" },
            };

            GitProcess git = new GitProcess(enlistment);

            Dictionary<string, GitConfigSetting> actualConfigSettings;
            if (!git.TryGetAllLocalConfig(out actualConfigSettings))
            {
                return false;
            }

            foreach (string key in expectedConfigSettings.Keys)
            {
                GitConfigSetting actualSetting;
                if (!actualConfigSettings.TryGetValue(key, out actualSetting) ||
                    !actualSetting.HasValue(expectedConfigSettings[key]))
                {
                    GitProcess.Result setConfigResult = git.SetInLocalConfig(key, expectedConfigSettings[key]);
                    if (setConfigResult.HasErrors)
                    {
                        return false;
                    }
                }
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
            verb.EnlistmentRootPath = enlistmentRootPath;
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

        protected bool ShowStatusWhileRunning(
            Func<bool> action, 
            string message, 
            bool suppressGvfsLogMessage = false)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                action,
                message,
                this.Output,
                showSpinner: !this.Unattended && this.Output == Console.Out && !ConsoleHelper.IsConsoleOutputRedirectedToFile(),
                gvfsLogEnlistmentRoot: suppressGvfsLogMessage ? null : Paths.GetGVFSEnlistmentRoot(this.EnlistmentRootPath),
                initialDelayMs: 0);
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

        protected void CheckGVFltHealthy()
        {
            string error;
            if (!GvFltFilter.IsHealthy(out error, tracer: null))
            {
                this.ReportErrorAndExit(tracer: null, error: error);
            }
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

        protected GVFSConfig QueryGVFSConfig(ITracer tracer, GVFSEnlistment enlistment, RetryConfig retryConfig)
        {
            GVFSConfig gvfsConfig = null;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                    {
                        return configRequestor.TryQueryGVFSConfig(out gvfsConfig);
                    }
                },
                "Querying remote for config",
                suppressGvfsLogMessage: true))
            {
                this.ReportErrorAndExit(tracer, "Unable to query /gvfs/config");
            }

            return gvfsConfig;
        }        

        protected void ValidateClientVersions(ITracer tracer, GVFSEnlistment enlistment, GVFSConfig gvfsConfig, bool showWarnings)
        {
            this.CheckGitVersion(tracer, enlistment);
            this.GetGVFSHooksPathAndCheckVersion(tracer);
            this.CheckVolumeSupportsDeleteNotifications(tracer, enlistment);

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

        protected string GetGVFSHooksPathAndCheckVersion(ITracer tracer)
        {
            string hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);
            if (hooksPath == null)
            {
                this.ReportErrorAndExit(tracer, "Could not find " + GVFSConstants.GVFSHooksExecutableName);
            }

            FileVersionInfo hooksFileVersionInfo = FileVersionInfo.GetVersionInfo(hooksPath + "\\" + GVFSConstants.GVFSHooksExecutableName);
            string gvfsVersion = ProcessHelper.GetCurrentProcessVersion();
            if (hooksFileVersionInfo.ProductVersion != gvfsVersion)
            {
                this.ReportErrorAndExit(tracer, "GVFS.Hooks version ({0}) does not match GVFS version ({1}).", hooksFileVersionInfo.ProductVersion, gvfsVersion);
            }

            return hooksPath;
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
            GVFSConfig gvfsConfig)
        {
            CacheServerInfo resolvedCacheServer = cacheServer;

            if (cacheServer.Url == null)
            {
                string cacheServerName = cacheServer.Name;
                string error = null;

                if (!cacheServerResolver.TryResolveUrlFromRemote(
                        cacheServerName,
                        gvfsConfig,
                        out resolvedCacheServer,
                        out error))
                {
                    this.ReportErrorAndExit(tracer, error);
                }
            }
            else if (cacheServer.Name.Equals(CacheServerInfo.ReservedNames.UserDefined))
            {
                resolvedCacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, gvfsConfig);
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
                catch
                {
                    this.ReportErrorAndExit("Invalid path: '{0}'", path);
                }
            }
        }

        protected bool TryDownloadCommit(
            string commitId,
            GVFSEnlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            GVFSGitObjects gitObjects,
            GitRepo repo,
            out string error)
        {
            if (!repo.CommitAndRootTreeExists(commitId))
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
                line => rootEntries.Add(DiffTreeResult.ParseFromLsTreeLine(line, repoRoot: string.Empty)),
                recursive: false);

            if (result.HasErrors)
            {
                error = "Error returned from ls-tree to find " + GVFSConstants.SpecialGitFiles.GitAttributes + " file: " + result.Errors;
                return false;
            }

            DiffTreeResult gitAttributes = rootEntries.FirstOrDefault(entry => entry.TargetFilename.Equals(GVFSConstants.SpecialGitFiles.GitAttributes));
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

        private string GetAlternatesPath(GVFSEnlistment enlistment)
        {
            return Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Info.Alternates);
        }

        private void CheckVolumeSupportsDeleteNotifications(ITracer tracer, Enlistment enlistment)
        {
            try
            {
                if (!NativeMethods.IsFeatureSupportedByVolume(Directory.GetDirectoryRoot(enlistment.EnlistmentRoot), NativeMethods.FileSystemFlags.FILE_RETURNS_CLEANUP_RESULT_INFO))
                {
                    this.ReportErrorAndExit(tracer, "Error: File system does not support features required by GVFS. Confirm that Windows version is at or beyond that required by GVFS");
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

        private void CheckGitVersion(ITracer tracer, Enlistment enlistment)
        {
            GitProcess.Result versionResult = GitProcess.Version(enlistment);
            if (versionResult.HasErrors)
            {
                this.ReportErrorAndExit(tracer, "Error: Unable to retrieve the git version");
            }

            GitVersion gitVersion;
            string version = versionResult.Output;
            if (version.StartsWith("git version "))
            {
                version = version.Substring(12);
            }

            if (!GitVersion.TryParseVersion(version, out gitVersion))
            {
                this.ReportErrorAndExit(tracer, "Error: Unable to parse the git version. {0}", version);
            }

            if (gitVersion.Platform != GVFSConstants.MinimumGitVersion.Platform)
            {
                this.ReportErrorAndExit(tracer, "Error: Invalid version of git {0}.  Must use gvfs version.", version);
            }

            if (gitVersion.IsLessThan(GVFSConstants.MinimumGitVersion))
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed git version {0} is less than the minimum version of {1}.",
                    gitVersion,
                    GVFSConstants.MinimumGitVersion);
            }
        }

        private bool TryValidateGVFSVersion(GVFSEnlistment enlistment, ITracer tracer, GVFSConfig config, out string errorMessage, out bool errorIsFatal)
        {
            errorMessage = null;
            errorIsFatal = false;

            using (ITracer activity = tracer.StartActivity("ValidateGVFSVersion", EventLevel.Informational))
            {
                Version currentVersion = new Version(ProcessHelper.GetCurrentProcessVersion());

                IEnumerable<GVFSConfig.VersionRange> allowedGvfsClientVersions =
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

                foreach (GVFSConfig.VersionRange versionRange in config.AllowedGVFSClientVersions)
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
            [Value(
                0,
                Required = false,
                Default = "",
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the GVFS enlistment root")]
            public override string EnlistmentRootPath { get; set; }

            public sealed override void Execute()
            {
                this.ValidatePathParameter(this.EnlistmentRootPath);

                this.PreCreateEnlistment();
                GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPath);
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
                GVFSConfig gvfsConfig,
                CacheServerInfo cacheServer)
            {
                string error;
                if (!RepoMetadata.TryInitialize(tracer, Path.Combine(enlistment.EnlistmentRoot, GVFSConstants.DotGVFS.Root), out error))
                {
                    this.ReportErrorAndExit(tracer, "Failed to initialize repo metadata: " + error);
                }

                this.InitializeLocalCacheAndObjectsPathsFromRepoMetadata(tracer, enlistment);

                // Note: Repos cloned with a version of GVFS that predates the local cache will not have a local cache configured
                if (!string.IsNullOrWhiteSpace(enlistment.LocalCacheRoot))
                {
                    this.EnsureLocalCacheIsHealthy(tracer, enlistment, retryConfig, gvfsConfig, cacheServer);
                }

                RepoMetadata.Shutdown();
            }

            private void InitializeLocalCacheAndObjectsPathsFromRepoMetadata(
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

                enlistment.InitializeLocalCacheAndObjectPaths(localCacheRoot, gitObjectsRoot);
            }

            private void EnsureLocalCacheIsHealthy(
                ITracer tracer,
                GVFSEnlistment enlistment,
                RetryConfig retryConfig,
                GVFSConfig gvfsConfig,
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
                                        if (string.Equals(alternatesLine, enlistment.GitObjectsRoot, StringComparison.OrdinalIgnoreCase))
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
                    if (gvfsConfig == null)
                    {
                        if (retryConfig == null)
                        {
                            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                            {
                                this.ReportErrorAndExit(tracer, "Failed to determine GVFS timeout and max retries: " + error);
                            }
                        }

                        gvfsConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);
                    }
                    
                    string localCacheKey;
                    LocalCacheResolver localCacheResolver = new LocalCacheResolver(enlistment);
                    if (!localCacheResolver.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers(
                        tracer,
                        gvfsConfig,
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
                    tracer.RelatedEvent(EventLevel.Informational, "GVFSVerb_InitializeLocalCacheAndObjectsPaths", metadata);
                    enlistment.InitializeLocalCacheAndObjectsPathsFromKey(enlistment.LocalCacheRoot, localCacheKey);

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating GitObjectsRoot ({enlistment.GitObjectsRoot}) and GitPackRoot ({enlistment.GitPackRoot})");
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
                        tracer.RelatedError(exceptionMetadata, $"{nameof(this.InitializeLocalCacheAndObjectsPaths)}: Exception while trying to create objects and pack folders");

                        this.ReportErrorAndExit(tracer, "Failed to create objects and pack folders");
                    }

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Creating new alternates file");
                    if (!this.TryCreateAlternatesFile(fileSystem, enlistment, out error))
                    {
                        this.ReportErrorAndExit(tracer, $"Failed to update alterates file with new objects path: {error}");
                    }

                    tracer.RelatedInfo($"{nameof(this.EnsureLocalCacheIsHealthy)}: Saving git objects root ({enlistment.GitObjectsRoot}) in repo metadata");
                    RepoMetadata.Instance.SetGitObjectsRoot(enlistment.GitObjectsRoot);
                }
            }

            private GVFSEnlistment CreateEnlistment(string enlistmentRootPath)
            {
                string gitBinPath = GitProcess.GetInstalledGitBinPath();
                if (string.IsNullOrWhiteSpace(gitBinPath))
                {
                    this.ReportErrorAndExit("Error: " + GVFSConstants.GitIsNotInstalledError);
                }

                string hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);
                if (hooksPath == null)
                {
                    this.ReportErrorAndExit("Could not find " + GVFSConstants.GVFSHooksExecutableName);
                }

                GVFSEnlistment enlistment = null;
                try
                {
                    enlistment = GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, hooksPath);
                    if (enlistment == null)
                    {
                        this.ReportErrorAndExit(
                            "Error: '{0}' is not a valid GVFS enlistment",
                            enlistmentRootPath);
                    }
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