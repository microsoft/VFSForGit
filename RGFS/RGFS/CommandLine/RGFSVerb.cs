using CommandLine;
using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RGFS.CommandLine
{
    public abstract class RGFSVerb
    {
        protected const string StartServiceInstructions = "Run 'sc start RGFS.Service' from an elevated command prompt to ensure it is running.";

        public RGFSVerb()
        {
            this.Output = Console.Out;
            this.ReturnCode = ReturnCode.Success;
            this.ServiceName = RGFSConstants.Service.ServiceName;

            this.Unattended = RGFSEnlistment.IsUnattended(tracer: null);

            this.InitializeDefaultParameterValues();
        }

        public abstract string EnlistmentRootPath { get; set; }

        [Option(
            RGFSConstants.VerbParameters.Mount.ServiceName,
            Default = RGFSConstants.Service.ServiceName,
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
            string expectedHooksPath = Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Hooks.Root);
            expectedHooksPath = expectedHooksPath.Replace('\\', '/');

            Dictionary<string, string> expectedConfigSettings = new Dictionary<string, string>
            {
                { "am.keepcr", "true" },
                { "core.autocrlf", "false" },
                { "core.fscache", "true" },
                { "core.rgfs", "true" },
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
            where TVerb : RGFSVerb, new()
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
            bool suppressRgfsLogMessage = false)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                action,
                message,
                this.Output,
                showSpinner: !this.Unattended && this.Output == Console.Out && !ConsoleHelper.IsConsoleOutputRedirectedToFile(),
                rgfsLogEnlistmentRoot: suppressRgfsLogMessage ? null : Paths.GetRGFSEnlistmentRoot(this.EnlistmentRootPath),
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

        protected RetryConfig GetRetryConfig(ITracer tracer, RGFSEnlistment enlistment, TimeSpan? timeoutOverride = null)
        {
            RetryConfig retryConfig;
            string error;
            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
            {
                this.ReportErrorAndExit(tracer, "Failed to determine RGFS timeout and max retries: " + error);
            }

            if (timeoutOverride.HasValue)
            {
                retryConfig.Timeout = timeoutOverride.Value;
            }

            return retryConfig;
        }

        protected RGFSConfig QueryRGFSConfig(ITracer tracer, RGFSEnlistment enlistment, RetryConfig retryConfig)
        {
            RGFSConfig rgfsConfig = null;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                    {
                        return configRequestor.TryQueryRGFSConfig(out rgfsConfig);
                    }
                },
                "Querying remote for config",
                suppressRgfsLogMessage: true))
            {
                this.ReportErrorAndExit("Unable to query /rgfs/config");
            }

            return rgfsConfig;
        }

        protected void ValidateClientVersions(ITracer tracer, RGFSEnlistment enlistment, RGFSConfig rgfsConfig, bool showWarnings)
        {
            this.CheckGitVersion(tracer, enlistment);
            this.GetRGFSHooksPathAndCheckVersion(tracer);
            this.CheckVolumeSupportsDeleteNotifications(tracer, enlistment);

            string errorMessage = null;
            bool errorIsFatal = false;
            if (!this.TryValidateRGFSVersion(enlistment, tracer, rgfsConfig, out errorMessage, out errorIsFatal))
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

        protected string GetRGFSHooksPathAndCheckVersion(ITracer tracer)
        {
            string hooksPath = ProcessHelper.WhereDirectory(RGFSConstants.RGFSHooksExecutableName);
            if (hooksPath == null)
            {
                this.ReportErrorAndExit(tracer, "Could not find " + RGFSConstants.RGFSHooksExecutableName);
            }

            FileVersionInfo hooksFileVersionInfo = FileVersionInfo.GetVersionInfo(hooksPath + "\\" + RGFSConstants.RGFSHooksExecutableName);
            string rgfsVersion = ProcessHelper.GetCurrentProcessVersion();
            if (hooksFileVersionInfo.ProductVersion != rgfsVersion)
            {
                this.ReportErrorAndExit(tracer, "RGFS.Hooks version ({0}) does not match RGFS version ({1}).", hooksFileVersionInfo.ProductVersion, rgfsVersion);
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

        protected CacheServerInfo ResolveCacheServerUrlIfNeeded(
            ITracer tracer,
            CacheServerInfo cacheServer,
            CacheServerResolver cacheServerResolver,
            RGFSConfig rgfsConfig)
        {
            CacheServerInfo resolvedCacheServer = cacheServer;

            if (cacheServer.Url == null)
            {
                string cacheServerName = cacheServer.Name;
                string error = null;

                if (!cacheServerResolver.TryResolveUrlFromRemote(
                        cacheServerName,
                        rgfsConfig,
                        out resolvedCacheServer,
                        out error))
                {
                    this.ReportErrorAndExit(tracer, error);
                }
            }

            this.Output.WriteLine("Using cache server: " + resolvedCacheServer);
            return resolvedCacheServer;
        }

        protected bool TryDownloadCommit(
            string commitId,
            RGFSEnlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            RGFSGitObjects gitObjects,
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

        protected bool TryDownloadRootGitAttributes(RGFSEnlistment enlistment, RGFSGitObjects gitObjects, GitRepo repo, out string error)
        {
            List<DiffTreeResult> rootEntries = new List<DiffTreeResult>();
            GitProcess git = new GitProcess(enlistment);
            GitProcess.Result result = git.LsTree(
                RGFSConstants.DotGit.HeadName,
                line => rootEntries.Add(DiffTreeResult.ParseFromLsTreeLine(line, repoRoot: string.Empty)),
                recursive: false);

            if (result.HasErrors)
            {
                error = "Error returned from ls-tree to find " + RGFSConstants.SpecialGitFiles.GitAttributes + " file: " + result.Errors;
                return false;
            }

            DiffTreeResult gitAttributes = rootEntries.FirstOrDefault(entry => entry.TargetFilename.Equals(RGFSConstants.SpecialGitFiles.GitAttributes));
            if (gitAttributes == null)
            {
                error = "This branch does not contain a " + RGFSConstants.SpecialGitFiles.GitAttributes + " file in the root folder.  This file is required by RGFS clone";
                return false;
            }

            if (!repo.ObjectExists(gitAttributes.TargetSha))
            {
                if (gitObjects.TryDownloadAndSaveObject(gitAttributes.TargetSha) != GitObjects.DownloadAndSaveObjectResult.Success)
                {
                    error = "Could not download " + RGFSConstants.SpecialGitFiles.GitAttributes + " file";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void CheckVolumeSupportsDeleteNotifications(ITracer tracer, Enlistment enlistment)
        {
            try
            {
                if (!NativeMethods.IsFeatureSupportedByVolume(Directory.GetDirectoryRoot(enlistment.EnlistmentRoot), NativeMethods.FileSystemFlags.FILE_RETURNS_CLEANUP_RESULT_INFO))
                {
                    this.ReportErrorAndExit(tracer, "Error: File system does not support features required by RGFS. Confirm that Windows version is at or beyond that required by RGFS");
                }
            }
            catch (VerbAbortedException)
            {
                // ReportErrorAndExit throws VerbAbortedException.  Catch and re-throw here so that RGFS does not report that
                // it failed to determine if file system supports required features
                throw;
            }
            catch (Exception e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Failed to determine if file system supports features required by RGFS");
                }

                this.ReportErrorAndExit(tracer, "Error: Failed to determine if file system supports features required by RGFS.");
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

            if (gitVersion.Platform != RGFSConstants.MinimumGitVersion.Platform)
            {
                this.ReportErrorAndExit(tracer, "Error: Invalid version of git {0}.  Must use rgfs version.", version);
            }

            if (gitVersion.IsLessThan(RGFSConstants.MinimumGitVersion))
            {
                this.ReportErrorAndExit(
                    tracer,
                    "Error: Installed git version {0} is less than the minimum version of {1}.",
                    gitVersion,
                    RGFSConstants.MinimumGitVersion);
            }
        }

        private bool TryValidateRGFSVersion(RGFSEnlistment enlistment, ITracer tracer, RGFSConfig config, out string errorMessage, out bool errorIsFatal)
        {
            errorMessage = null;
            errorIsFatal = false;

            using (ITracer activity = tracer.StartActivity("ValidateRGFSVersion", EventLevel.Informational))
            {
                Version currentVersion = new Version(ProcessHelper.GetCurrentProcessVersion());

                IEnumerable<RGFSConfig.VersionRange> allowedRgfsClientVersions =
                    config != null
                    ? config.AllowedRGFSClientVersions
                    : null;

                if (allowedRgfsClientVersions == null || !allowedRgfsClientVersions.Any())
                {
                    errorMessage = "WARNING: Unable to validate your RGFS version" + Environment.NewLine;
                    if (config == null)
                    {
                        errorMessage += "Could not query valid RGFS versions from: " + Uri.EscapeUriString(enlistment.RepoUrl);
                    }
                    else
                    {
                        errorMessage += "Server not configured to provide supported RGFS versions";
                    }

                    EventMetadata metadata = new EventMetadata();
                    tracer.RelatedError(metadata, errorMessage, Keywords.Network);

                    return false;
                }

                foreach (RGFSConfig.VersionRange versionRange in config.AllowedRGFSClientVersions)
                {
                    if (currentVersion >= versionRange.Min &&
                        (versionRange.Max == null || currentVersion <= versionRange.Max))
                    {
                        activity.RelatedEvent(
                            EventLevel.Informational,
                            "RGFSVersionValidated",
                            new EventMetadata
                            {
                                { "SupportedVersionRange", versionRange },
                            });

                        return true;
                    }
                }

                activity.RelatedError("RGFS version {0} is not supported", currentVersion);
            }

            errorMessage = "ERROR: Your RGFS version is no longer supported.  Install the latest and try again.";
            errorIsFatal = true;
            return false;
        }

        public abstract class ForExistingEnlistment : RGFSVerb
        {
            [Value(
                0,
                Required = false,
                Default = "",
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the RGFS enlistment root")]
            public override string EnlistmentRootPath { get; set; }

            public sealed override void Execute()
            {
                this.PreCreateEnlistment();
                RGFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPath);
                this.Execute(enlistment);
            }

            protected virtual void PreCreateEnlistment()
            {
            }

            protected abstract void Execute(RGFSEnlistment enlistment);

            private RGFSEnlistment CreateEnlistment(string enlistmentRootPath)
            {
                string gitBinPath = GitProcess.GetInstalledGitBinPath();
                if (string.IsNullOrWhiteSpace(gitBinPath))
                {
                    this.ReportErrorAndExit("Error: " + RGFSConstants.GitIsNotInstalledError);
                }

                string hooksPath = ProcessHelper.WhereDirectory(RGFSConstants.RGFSHooksExecutableName);
                if (hooksPath == null)
                {
                    this.ReportErrorAndExit("Could not find " + RGFSConstants.RGFSHooksExecutableName);
                }

                RGFSEnlistment enlistment = null;
                try
                {
                    enlistment = RGFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, hooksPath);
                    if (enlistment == null)
                    {
                        this.ReportErrorAndExit(
                            "Error: '{0}' is not a valid RGFS enlistment",
                            enlistmentRootPath);
                    }
                }
                catch (InvalidRepoException e)
                {
                    this.ReportErrorAndExit(
                        "Error: '{0}' is not a valid RGFS enlistment. {1}",
                        enlistmentRootPath,
                        e.Message);
                }

                return enlistment;
            }
        }

        public class VerbAbortedException : Exception
        {
            public VerbAbortedException(RGFSVerb verb)
            {
                this.Verb = verb;
            }

            public RGFSVerb Verb { get; }
        }
    }
}