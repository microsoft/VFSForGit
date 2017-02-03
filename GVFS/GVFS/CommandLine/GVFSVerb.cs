using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;

namespace GVFS.CommandLine
{
    public abstract class GVFSVerb
    {
        public GVFSVerb()
        {
            this.Output = Console.Out;
            this.ReturnCode = ReturnCode.Success;

            this.InitializeDefaultParameterValues();
        }

        public abstract string EnlistmentRootPath { get; set; }

        public TextWriter Output { get; set; }

        public ReturnCode ReturnCode { get; private set; }

        protected abstract string VerbName { get; }

        public static bool TrySetGitConfigSettings(GitProcess git)
        {
            Dictionary<string, string> expectedConfigSettings = new Dictionary<string, string>
            {
                { "core.autocrlf", "false" },
                { "core.fscache", "true" },
                { "core.gvfs", "true" },
                { "core.preloadIndex", "true" },
                { "core.safecrlf", "false" },
                { "core.sparseCheckout", "true" },
                { GVFSConstants.VirtualizeObjectsGitConfigName, "true" },
                { "credential.validate", "false" },
                { "diff.autoRefreshIndex", "false" },
                { "gc.auto", "0" },
                { "merge.stat", "false" },
                { "receive.autogc", "false" },
            };

            GitProcess.Result getConfigResult = git.GetAllLocalConfig();
            if (getConfigResult.HasErrors)
            {
                return false;
            }

            Dictionary<string, string> actualConfigSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in getConfigResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (!actualConfigSettings.ContainsKey(fields[0]) && fields.Length == 2)
                {
                    actualConfigSettings.Add(fields[0], fields[1]);
                }
                else
                {
                    actualConfigSettings[fields[0]] = null;
                }
            }

            foreach (string key in expectedConfigSettings.Keys)
            {
                string actualValue;
                if (!actualConfigSettings.TryGetValue(key, out actualValue) ||
                    actualValue != expectedConfigSettings[key])
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

        public abstract void Execute(ITracer tracer = null);

        public virtual void InitializeDefaultParameterValues()
        {
        }

        protected void ReportErrorAndExit(string error, params object[] args)
        {
            if (error != null)
            {
                this.Output.WriteLine(error, args);
            }

            this.ReturnCode = ReturnCode.GenericError;
            throw new VerbAbortedException(this);
        }

        protected void CheckElevated()
        {
            if (!ProcessHelper.IsAdminElevated())
            {
                this.ReportErrorAndExit("{0} must be run with elevated privileges", this.VerbName);
            }
        }

        protected void CheckGVFltRunning()
        {
            bool gvfltServiceRunning = false;
            try
            {
                ServiceController controller = new ServiceController("gvflt");
                gvfltServiceRunning = controller.Status.Equals(ServiceControllerStatus.Running);
            }
            catch (InvalidOperationException)
            {
                this.ReportErrorAndExit("Error: GVFlt Service was not found. To resolve, re-install GVFS");
            }

            if (!gvfltServiceRunning)
            {
                this.ReportErrorAndExit("Error: GVFlt Service is not running. To resolve, run \"sc start gvflt\" from an admin command prompt");
            }
        }

        protected void CheckGitVersion(Enlistment enlistment)
        {
            GitProcess.Result versionResult = GitProcess.Version(enlistment);
            if (versionResult.HasErrors)
            {
                this.ReportErrorAndExit("Error: Unable to retrieve the git version");
            }

            GitVersion gitVersion;
            string version = versionResult.Output;
            if (version.StartsWith("git version "))
            {
                version = version.Substring(12);
            }

            if (!GitVersion.TryParse(version, out gitVersion))
            {
                this.ReportErrorAndExit("Error: Unable to parse the git version. {0}", version);
            }

            if (gitVersion.Platform != GVFSConstants.MinimumGitVersion.Platform)
            {
                this.ReportErrorAndExit("Error: Invalid version of git {0}.  Must use gvfs version.", version);
            }

            if (gitVersion.IsLessThan(GVFSConstants.MinimumGitVersion))
            {
                this.ReportErrorAndExit(
                    "Error: Installed git version {0} is less than the minimum version of {1}.",
                    gitVersion,
                    GVFSConstants.MinimumGitVersion);
            }
        }

        protected void CheckAntiVirusExclusion(GVFSEnlistment enlistment)
        {
            bool isExcluded;
            if (AntiVirusExclusions.TryGetIsPathExcluded(enlistment.EnlistmentRoot, out isExcluded))
            {
                if (!isExcluded)
                {
                    if (ProcessHelper.IsAdminElevated())
                    {
                        this.Output.WriteLine();
                        this.Output.WriteLine("Adding {0} to your antivirus exclusion list", enlistment.EnlistmentRoot);
                        this.Output.WriteLine();

                        AntiVirusExclusions.AddAntiVirusExclusion(enlistment.EnlistmentRoot);

                        if (!AntiVirusExclusions.TryGetIsPathExcluded(enlistment.EnlistmentRoot, out isExcluded) ||
                            !isExcluded)
                        {
                            this.ReportErrorAndExit(
                                "This repo is not excluded from antivirus and we were unable to add it.  Add '{0}' to your exclusion list and then run {1} again.",
                                enlistment.EnlistmentRoot,
                                this.VerbName);
                        }
                    }
                    else
                    {
                        this.ReportErrorAndExit(
                            "This repo is not excluded from antivirus.  Either re-run {1} with elevated privileges, or add '{0}' to your exclusion list and then run {1} again.",
                            enlistment.EnlistmentRoot,
                            this.VerbName);
                    }
                }
            }
            else
            {
                this.Output.WriteLine();
                this.Output.WriteLine(
                    "WARNING: Unable to determine if this repo is excluded from antivirus.  Please check to ensure that '{0}' is excluded.",
                    enlistment.EnlistmentRoot);
                this.Output.WriteLine();
            }
        }

        protected void ValidateGVFSVersion(GVFSEnlistment enlistment, HttpGitObjects httpGitObjects, ITracer tracer)
        {
            using (ITracer activity = tracer.StartActivity("ValidateGVFSVersion", EventLevel.Informational))
            {
                Version currentVersion = new Version(ProcessHelper.GetCurrentProcessVersion());

                GVFSConfigResponse config = httpGitObjects.QueryGVFSConfig();
                IEnumerable<GVFSConfigResponse.VersionRange> allowedGvfsClientVersions =
                    config != null
                    ? config.AllowedGvfsClientVersions
                    : null;

                if (allowedGvfsClientVersions == null || !allowedGvfsClientVersions.Any())
                {
                    string errorMessage = string.Empty;
                    if (config == null)
                    {
                        errorMessage = "Could not query valid GVFS versions from: " + Uri.EscapeUriString(enlistment.RepoUrl);
                    }
                    else
                    {
                        errorMessage = "Server not configured to provide supported GVFS versions";
                    }

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("ErrorMessage", errorMessage);
                    tracer.RelatedError(metadata, Keywords.Network);

                    this.Output.WriteLine();
                    this.Output.WriteLine("WARNING: Unable to validate your GVFS version");
                    this.Output.WriteLine();
                    return;
                }

                foreach (GVFSConfigResponse.VersionRange versionRange in config.AllowedGvfsClientVersions)
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
                        return;
                    }
                }

                activity.RelatedError("GVFS version {0} is not supported", currentVersion);
            }

            this.ReportErrorAndExit("\r\nERROR: Your GVFS version is no longer supported.  Install the latest and try again.\r\n");
        }

        private GVFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                this.ReportErrorAndExit("Error: " + GVFSConstants.GitIsNotInstalledError);
            }

            if (string.IsNullOrWhiteSpace(enlistmentRootPath))
            {
                enlistmentRootPath = Environment.CurrentDirectory;
            }

            string hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);
            if (hooksPath == null)
            {
                this.ReportErrorAndExit("Could not find " + GVFSConstants.GVFSHooksExecutableName);
            }

            GVFSEnlistment enlistment = null;
            try
            {
                enlistment = GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, null, gitBinPath, hooksPath);
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

        public abstract class ForExistingEnlistment : GVFSVerb
        {
            [Value(
                0,
                Required = false,
                Default = "",
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the GVFS enlistment root")]
            public override string EnlistmentRootPath { get; set; }

            public sealed override void Execute(ITracer tracer = null)
            {
                this.PreExecute(this.EnlistmentRootPath, tracer);
                GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPath);
                this.Execute(enlistment, tracer);
            }

            protected virtual void PreExecute(string enlistmentRootPath, ITracer tracer = null)
            {
            }

            protected abstract void Execute(GVFSEnlistment enlistment, ITracer tracer = null);
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
