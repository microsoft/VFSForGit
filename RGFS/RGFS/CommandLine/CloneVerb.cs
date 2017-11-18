using CommandLine;
using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.NamedPipes;
using RGFS.Common.Tracing;
using RGFS.GVFlt;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RGFS.CommandLine
{
    [Verb(CloneVerb.CloneVerbName, HelpText = "Clone a git repo and mount it as a RGFS virtual repo")]
    public class CloneVerb : RGFSVerb
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
            HelpText = "Full or relative path to the RGFS enlistment root")]
        public override string EnlistmentRootPath { get; set; }

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
            this.BlockEmptyCacheServerUrl(this.CacheServerUrl);
           
            try
            {
                RGFSEnlistment enlistment;
                Result cloneResult = new Result(false);

                CacheServerInfo cacheServer = null;

                using (JsonEtwTracer tracer = new JsonEtwTracer(RGFSConstants.RGFSEtwProviderName, "RGFSClone"))
                {
                    cloneResult = this.TryCreateEnlistment(out enlistment);
                    if (cloneResult.Success)
                    {
                        tracer.AddLogFileEventListener(
                            RGFSEnlistment.GetNewRGFSLogFileName(enlistment.RGFSLogsRoot, RGFSConstants.LogFileTypes.Clone),
                            EventLevel.Informational,
                            Keywords.Any);
                        tracer.WriteStartEvent(
                            enlistment.EnlistmentRoot,
                            enlistment.RepoUrl,
                            this.CacheServerUrl,
                            enlistment.GitObjectsRoot,
                            new EventMetadata
                            {
                                { "Branch", this.Branch },
                                { "SingleBranch", this.SingleBranch },
                                { "NoMount", this.NoMount },
                                { "NoPrefetch", this.NoPrefetch },
                                { "Unattended", this.Unattended },
                                { "IsElevated", ProcessHelper.IsAdminElevated() },
                            });

                        CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                        cacheServer = cacheServerResolver.ParseUrlOrFriendlyName(this.CacheServerUrl);

                        this.Output.WriteLine("Clone parameters:");
                        this.Output.WriteLine("  Repo URL:     " + enlistment.RepoUrl);
                        this.Output.WriteLine("  Cache Server: " + cacheServer);
                        this.Output.WriteLine("  Destination:  " + enlistment.EnlistmentRoot);

                        string authErrorMessage = null;
                        if (!this.ShowStatusWhileRunning(
                            () => enlistment.Authentication.TryRefreshCredentials(tracer, out authErrorMessage),
                            "Authenticating"))
                        {
                            this.ReportErrorAndExit(tracer, "Unable to clone because authentication failed");
                        }

                        RetryConfig retryConfig = this.GetRetryConfig(tracer, enlistment, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));
                        RGFSConfig rgfsConfig = this.QueryRGFSConfig(tracer, enlistment, retryConfig);

                        cacheServer = this.ResolveCacheServerUrlIfNeeded(tracer, cacheServer, cacheServerResolver, rgfsConfig);
                        this.ValidateClientVersions(tracer, enlistment, rgfsConfig, showWarnings: true);

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
                        this.Execute<PrefetchVerb>(
                            this.EnlistmentRootPath,
                            verb =>
                            {
                                verb.Commits = true;
                                verb.SkipVersionCheck = true;
                                verb.ResolvedCacheServer = cacheServer;
                            });
                    }

                    if (this.NoMount)
                    {
                        this.Output.WriteLine("\r\nIn order to mount, first cd to within your enlistment, then call: ");
                        this.Output.WriteLine("rgfs mount");
                    }
                    else
                    {
                        this.Execute<MountVerb>(
                            this.EnlistmentRootPath,
                            verb =>
                            {
                                verb.SkipMountedCheck = true;
                                verb.SkipVersionCheck = true;
                            });
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

        private static bool IsForceCheckoutErrorCloneFailure(string checkoutError)
        {
            if (string.IsNullOrWhiteSpace(checkoutError) ||
                checkoutError.Contains("Already on"))
            {
                return false;
            }

            return true;
        }

        private Result TryCreateEnlistment(out RGFSEnlistment enlistment)
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
                return new Result(RGFSConstants.GitIsNotInstalledError);
            }
            
            string hooksPath = this.GetRGFSHooksPathAndCheckVersion(tracer: null);

            enlistment = new RGFSEnlistment(
                this.EnlistmentRootPath,
                this.RepositoryURL,
                gitBinPath,
                hooksPath);

            return new Result(true);
        }
        
        private Result TryClone(JsonEtwTracer tracer, RGFSEnlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig)
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
                    
                    return this.CreateClone(tracer, enlistment, objectRequestor, refs, this.Branch);
                }
            }
        }

        private NamedPipeServer StartNamedPipe(ITracer tracer, RGFSEnlistment enlistment, out Result errorResult)
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
            string enlistmentRoot = Paths.GetRGFSEnlistmentRoot(this.EnlistmentRootPath);
            if (enlistmentRoot != null)
            {
                this.ReportErrorAndExit("Error: You can't clone inside an existing RGFS repo ({0})", enlistmentRoot);
            }
        }

        private Result CreateClone(
            ITracer tracer,
            RGFSEnlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            GitRefs refs,
            string branch)
        {
            Result initRepoResult = this.TryInitRepo(tracer, refs, enlistment);
            if (!initRepoResult.Success)
            {
                return initRepoResult;
            }

            string errorMessage;
            if (!enlistment.TryConfigureAlternate(out errorMessage))
            {
                return new Result("Error configuring alternate: " + errorMessage);
            }

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem);
            RGFSGitObjects gitObjects = new RGFSGitObjects(new RGFSContext(tracer, fileSystem, gitRepo, enlistment), objectRequestor);

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

            if (!RGFSVerb.TrySetGitConfigSettings(enlistment))
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
            if (createBranchResult.HasErrors)
            {
                return new Result("Unable to create branch '" + originBranchName + "': " + createBranchResult.Errors + "\r\n" + createBranchResult.Output);
            }

            File.WriteAllText(
                Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Head),
                "ref: refs/heads/" + branch);

            File.AppendAllText(
                Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.SparseCheckoutPath),
                RGFSConstants.GitPathSeparatorString + RGFSConstants.SpecialGitFiles.GitAttributes + "\n");

            if (!this.TryDownloadRootGitAttributes(enlistment, gitObjects, gitRepo, out errorMessage))
            {
                return new Result(errorMessage);
            }

            this.CreateGitScript(enlistment);

            GitProcess.Result forceCheckoutResult = git.ForceCheckout(branch);
            if (forceCheckoutResult.HasErrors)
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

            GitProcess.Result updateIndexresult = git.UpdateIndexVersion4();
            if (updateIndexresult.HasErrors)
            {
                string error = "Could not update index, error: " + updateIndexresult.Errors;
                tracer.RelatedError(error);
                return new Result(error);
            }

            string installHooksError;
            if (!HooksInstaller.InstallHooks(enlistment, out installHooksError))
            {
                tracer.RelatedError(installHooksError);
                return new Result(installHooksError);
            }

            if (!RepoMetadata.TryInitialize(tracer, enlistment.DotRGFSRoot, out errorMessage))
            {
                tracer.RelatedError(errorMessage);
                return new Result(errorMessage);
            }

            try
            {
                RepoMetadata.Instance.SaveCurrentDiskLayoutVersion();
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

            // Prepare the working directory folder for RGFS last to ensure that rgfs mount will fail if rgfs clone has failed
            string prepGVFltError;
            if (!GVFltCallbacks.TryPrepareFolderForGVFltCallbacks(enlistment.WorkingDirectoryRoot, out prepGVFltError))
            {
                tracer.RelatedError(prepGVFltError);
                return new Result(prepGVFltError);
            }

            return new Result(true);
        }

        private void CreateGitScript(RGFSEnlistment enlistment)
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
echo      This repo was cloned using RGFS, and the git repo is in the 'src' directory     
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
            string repoPath = enlistmentToInit.WorkingDirectoryRoot;
            GitProcess.Result initResult = GitProcess.Init(enlistmentToInit);
            if (initResult.HasErrors)
            {
                string error = string.Format("Could not init repo at to {0}: {1}", repoPath, initResult.Errors);
                tracer.RelatedError(error);
                return new Result(error);
            }

            GitProcess.Result remoteAddResult = new GitProcess(enlistmentToInit).RemoteAdd("origin", enlistmentToInit.RepoUrl);
            if (remoteAddResult.HasErrors)
            {
                string error = string.Format("Could not add remote to {0}: {1}", repoPath, remoteAddResult.Errors);
                tracer.RelatedError(error);
                return new Result(error);
            }

            File.WriteAllText(
                Path.Combine(repoPath, RGFSConstants.DotGit.PackedRefs),
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