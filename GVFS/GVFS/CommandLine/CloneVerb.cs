using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
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

        public override void Execute()
        {
            int exitCode = 0;

            this.EnlistmentRootPath = this.GetCloneRoot();

            this.CheckGVFltHealthy();
            this.CheckNotInsideExistingRepo();
            
            try
            {
                GVFSEnlistment enlistment;
                Result cloneResult = new Result(false);

                using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "GVFSClone"))
                {
                    cloneResult = this.TryCreateEnlistment(out enlistment);
                    if (cloneResult.Success)
                    {
                        tracer.AddLogFileEventListener(
                            GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Clone),
                            EventLevel.Informational,
                            Keywords.Any);
                        
                        string authErrorMessage = null;
                        if (!this.ShowStatusWhileRunning(
                            () => enlistment.Authentication.TryRefreshCredentials(tracer, out authErrorMessage),
                            "Authenticating"))
                        {
                            this.ReportErrorAndExit("Unable to clone because authentication failed");
                        }

                        RetryConfig retryConfig;
                        string error;
                        if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                        {
                            this.ReportErrorAndExit("Failed to determine GVFS timeout and max retries: " + error);
                        }

                        retryConfig.Timeout = TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes);

                        GVFSConfig gvfsConfig;
                        CacheServerInfo cacheServer;
                        using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                        {
                            gvfsConfig = configRequestor.QueryGVFSConfig();
                        }

                        if (!CacheServerInfo.TryDetermineCacheServer(this.CacheServerUrl, enlistment, gvfsConfig.CacheServers, out cacheServer, out error))
                        {
                            this.ReportErrorAndExit(error);
                        }
                        
                        tracer.WriteStartEvent(
                            enlistment.EnlistmentRoot,
                            enlistment.RepoUrl,
                            cacheServer.Url,
                            new EventMetadata
                            {
                                { "Branch", this.Branch },
                                { "SingleBranch", this.SingleBranch },
                                { "NoMount", this.NoMount },
                                { "NoPrefetch", this.NoPrefetch }
                            });

                        this.Output.WriteLine("Clone parameters:");
                        this.Output.WriteLine("  Repo URL:     " + enlistment.RepoUrl);
                        this.Output.WriteLine("  Cache Server: " + cacheServer);
                        this.Output.WriteLine("  Destination:  " + enlistment.EnlistmentRoot);
                        
                        this.ValidateClientVersions(tracer, enlistment, gvfsConfig);

                        this.ShowStatusWhileRunning(
                            () =>
                            {
                                cloneResult = this.TryClone(tracer, enlistment, cacheServer, retryConfig);
                                return cloneResult.Success;
                            },
                            "Cloning");
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
                        PrefetchVerb prefetch = new PrefetchVerb();
                        prefetch.EnlistmentRootPath = this.EnlistmentRootPath;
                        prefetch.Commits = true;
                        prefetch.Execute();
                    }

                    if (this.NoMount)
                    {
                        this.Output.WriteLine("\r\nIn order to mount, first cd to within your enlistment, then call: ");
                        this.Output.WriteLine("gvfs mount");
                    }
                    else
                    {
                        MountVerb mount = new MountVerb();
                        mount.EnlistmentRootPath = this.EnlistmentRootPath;
                        mount.SkipMountedCheck = true;
                        mount.SkipVersionCheck = true;
                        mount.ServiceName = this.ServiceName;

                        mount.Execute();
                    }
                }
                else
                {
                    this.Output.WriteLine("\r\nCannot clone @ {0}", this.EnlistmentRootPath);
                    this.Output.WriteLine("Error: {0}", cloneResult.ErrorMessage);
                    exitCode = (int)ReturnCode.GenericError;
                }
            }
            catch (AggregateException e)
            {
                this.Output.WriteLine("Cannot clone @ {0}:", this.EnlistmentRootPath);
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
                this.ReportErrorAndExit("Cannot clone @ {0}: {1}", this.EnlistmentRootPath, e.ToString());
            }

            Environment.Exit(exitCode);
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
            
            string hooksPath = this.GetGVFSHooksPathAndCheckVersion();

            enlistment = new GVFSEnlistment(
                this.EnlistmentRootPath,
                this.RepositoryURL,
                gitBinPath,
                hooksPath);

            return new Result(true);
        }
        
        private Result TryClone(JsonEtwTracer tracer, GVFSEnlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig)
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

                    if (!enlistment.TryCreateEnlistmentFolders())
                    {
                        string error = "Could not create enlistment directory";
                        tracer.RelatedError(error);
                        return new Result(error);
                    }
                    
                    CloneHelper cloneHelper = new CloneHelper(tracer, enlistment, objectRequestor);
                    return cloneHelper.CreateClone(refs, this.Branch);
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

        private string GetCloneRoot()
        {
            try
            {
                string repoName = this.RepositoryURL.Substring(this.RepositoryURL.LastIndexOf('/') + 1);
                string cloneRoot =
                    string.IsNullOrWhiteSpace(this.EnlistmentRootPath)
                    ? Path.Combine(Environment.CurrentDirectory, repoName)
                    : this.EnlistmentRootPath;

                return Path.GetFullPath(cloneRoot);
            }
            catch (IOException e)
            {
                this.ReportErrorAndExit("Unable to determine clone root: " + e.ToString());
                return null;
            }
        }

        private void CheckNotInsideExistingRepo()
        {
            string enlistmentRoot = Paths.GetGVFSEnlistmentRoot(this.EnlistmentRootPath);
            if (enlistmentRoot != null)
            {
                this.ReportErrorAndExit("Error: You can't clone inside an existing GVFS repo ({0})", enlistmentRoot);
            }
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