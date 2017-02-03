using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(CloneVerb.CloneVerbName, HelpText = "Clone a git repo and mount it as a GVFS virtual repo")]
    public class CloneVerb : GVFSVerb
    {
        public const string CloneVerbName = "clone";
        private const string MountVerb = "gvfs mount";

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
        public override string EnlistmentRootPath { get; set; }

        [Option(
            "cache-server-url",
            Required = false,
            Default = "",
            HelpText = "Defines the url of the cache server")]
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

        protected override string VerbName
        {
            get { return CloneVerbName; }
        }

        public override void Execute(ITracer tracer = null)
        {
            if (tracer != null)
            {
                throw new InvalidOperationException("Clone does not support being called with an existing tracer");
            }

            this.CheckElevated();            
            this.CheckGVFltRunning();

            string fullPath = GVFSEnlistment.ToFullPath(this.EnlistmentRootPath, this.GetDefaultEnlistmentRoot());
            if (fullPath == null)
            {
                this.ReportErrorAndExit("Unable to write to directory " + this.EnlistmentRootPath);
            }

            this.EnlistmentRootPath = fullPath;

            this.Output.WriteLine();
            this.Output.WriteLine("Starting clone of {0} into {1}...", this.RepositoryURL, this.EnlistmentRootPath);
            this.Output.WriteLine();

            this.CacheServerUrl = Enlistment.StripObjectsEndpointSuffix(this.CacheServerUrl);

            try
            {
                GVFSEnlistment enlistment;

                Result cloneResult = this.TryCreateEnlistment(out enlistment);
                if (cloneResult.Success)
                {
                    using (JsonEtwTracer cloneTracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "GVFSClone"))
                    {
                        cloneTracer.AddLogFileEventListener(
                            GVFSEnlistment.GetNewGVFSLogFileName(
                                Path.Combine(this.EnlistmentRootPath, GVFSConstants.DotGVFSPath, GVFSConstants.GVFSLogFolderName),
                                this.VerbName),
                            EventLevel.Informational,
                            Keywords.Any);
                        cloneTracer.AddConsoleEventListener(EventLevel.Informational, Keywords.Any);
                        cloneTracer.WriteStartEvent(
                            enlistment.EnlistmentRoot,
                            enlistment.RepoUrl,
                            enlistment.CacheServerUrl,
                            new EventMetadata
                            {
                                    { "Branch", this.Branch },
                                    { "SingleBranch", this.SingleBranch },
                                    { "NoMount", this.NoMount },
                                    { "NoPrefetch", this.NoPrefetch }
                            });

                        cloneResult = this.TryClone(cloneTracer, enlistment);

                        if (cloneResult.Success)
                        {
                            this.Output.WriteLine("GVFS Enlistment created @ {0}", this.EnlistmentRootPath);

                            if (!this.NoPrefetch)
                            {
                                PrefetchVerb prefetch = new PrefetchVerb();
                                prefetch.EnlistmentRootPath = this.EnlistmentRootPath;
                                prefetch.Commits = true;
                                prefetch.Execute(cloneTracer);
                            }

                            if (this.NoMount)
                            {
                                this.Output.WriteLine("\r\nIn order to mount, first cd to within your enlistment, then call: ");
                                this.Output.WriteLine(CloneVerb.MountVerb);
                            }
                            else
                            {
                                MountVerb mount = new MountVerb();
                                mount.EnlistmentRootPath = this.EnlistmentRootPath;

                                // Tracer will be disposed in mount.Execute to avoid conflicts with the background process.
                                mount.Execute(cloneTracer);
                            }
                        }
                    }
                }

                // Write to the output after the tracer is disposed so that the error is the last message
                // displayed to the user
                if (!cloneResult.Success)
                {
                    this.Output.WriteLine("\r\nCannot clone @ {0}", this.EnlistmentRootPath);
                    this.Output.WriteLine("Error: {0}", cloneResult.ErrorMessage);
                    Environment.ExitCode = (int)ReturnCode.GenericError;
                }
            }
            catch (AggregateException e)
            {
                this.Output.WriteLine("Cannot clone @ {0}:", this.EnlistmentRootPath);
                foreach (Exception ex in e.Flatten().InnerExceptions)
                {
                    this.Output.WriteLine("Exception: {0}", ex.ToString());
                }

                Environment.ExitCode = (int)ReturnCode.GenericError;
            }
            catch (VerbAbortedException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.ReportErrorAndExit("Cannot clone @ {0}: {1}", this.EnlistmentRootPath, e.ToString());
            }
        }

        private Result TryCreateEnlistment(out GVFSEnlistment enlistment)
        {
            enlistment = null;

            // Check that EnlistmentRootPath is empty before creating a tracer and LogFileEventListener as 
            // LogFileEventListener will create a file in EnlistmentRootPath
            if (Directory.Exists(this.EnlistmentRootPath) && Directory.EnumerateFileSystemEntries(this.EnlistmentRootPath).Any())
            {
                return new Result("Clone directory '" + this.EnlistmentRootPath + "' exists and is not empty");
            }

            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                return new Result(GVFSConstants.GitIsNotInstalledError);
            }

            string hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);
            if (hooksPath == null)
            {
                this.ReportErrorAndExit("Could not find " + GVFSConstants.GVFSHooksExecutableName);
            }

            enlistment = new GVFSEnlistment(
                this.EnlistmentRootPath,
                this.RepositoryURL,
                this.CacheServerUrl,
                gitBinPath,
                hooksPath);

            return new Result(true);
        }

        private Result TryClone(JsonEtwTracer tracer, GVFSEnlistment enlistment)
        {
            this.CheckGitVersion(enlistment);

            HttpGitObjects httpGitObjects = new HttpGitObjects(tracer, enlistment, Environment.ProcessorCount);
            this.ValidateGVFSVersion(enlistment, httpGitObjects, tracer);

            GitRefs refs = httpGitObjects.QueryInfoRefs(this.SingleBranch ? this.Branch : null);

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

            if (!enlistment.TryCreateEnlistmentFolders())
            {
                string error = "Could not create enlistment directory";
                tracer.RelatedError(error);
                return new Result(error);
            }

            this.CheckAntiVirusExclusion(enlistment);

            CloneHelper cloneHelper = new CloneHelper(tracer, enlistment, httpGitObjects);
            return cloneHelper.CreateClone(refs, this.Branch);
        }

        private string GetDefaultEnlistmentRoot()
        {
            string repoName = this.RepositoryURL.Substring(this.RepositoryURL.LastIndexOf('/') + 1);
            return Path.Combine(Directory.GetCurrentDirectory(), repoName);
        }

        public class Result
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
