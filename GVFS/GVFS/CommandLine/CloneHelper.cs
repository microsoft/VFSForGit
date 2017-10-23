using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.GVFlt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.CommandLine
{
    public class CloneHelper
    {
        private GVFSEnlistment enlistment;
        private GitObjectsHttpRequestor objectRequestor;
        private ITracer tracer;

        public CloneHelper(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.objectRequestor = objectRequestor;
        }

        public CloneVerb.Result CreateClone(GitRefs refs, string branch)
        {
            CloneVerb.Result initRepoResult = this.TryInitRepo(refs, this.enlistment);
            if (!initRepoResult.Success)
            {
                return initRepoResult;
            }

            string errorMessage;
            if (!this.enlistment.TryConfigureAlternate(out errorMessage))
            {
                return new CloneVerb.Result("Error configuring alternate: " + errorMessage);
            }

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(this.tracer, this.enlistment, fileSystem);
            GVFSGitObjects gitObjects = new GVFSGitObjects(new GVFSContext(this.tracer, fileSystem, gitRepo, this.enlistment), this.objectRequestor);

            if (!gitObjects.TryEnsureCommitIsLocal(refs.GetTipCommitId(branch), commitDepth: 2))
            {
                return new CloneVerb.Result("Could not download tip commits from: " + Uri.EscapeUriString(this.objectRequestor.CacheServer.ObjectsEndpointUrl));
            }

            if (!GVFSVerb.TrySetGitConfigSettings(this.enlistment))
            {
                return new CloneVerb.Result("Unable to configure git repo");
            }
            
            CacheServerResolver cacheServerResolver = new CacheServerResolver(this.tracer, this.enlistment);
            if (!cacheServerResolver.TrySaveUrlToLocalConfig(this.objectRequestor.CacheServer, out errorMessage))
            {
                return new CloneVerb.Result("Unable to configure cache server: " + errorMessage);
            }

            GitProcess git = new GitProcess(this.enlistment);
            string originBranchName = "origin/" + branch;
            GitProcess.Result createBranchResult = git.CreateBranchWithUpstream(branch, originBranchName);
            if (createBranchResult.HasErrors)
            {
                return new CloneVerb.Result("Unable to create branch '" + originBranchName + "': " + createBranchResult.Errors + "\r\n" + createBranchResult.Output);
            }

            File.WriteAllText(
                Path.Combine(this.enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Head),
                "ref: refs/heads/" + branch);

            File.AppendAllText(
                Path.Combine(this.enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath),
                GVFSConstants.GitPathSeparatorString + GVFSConstants.SpecialGitFiles.GitAttributes + "\n");

            CloneVerb.Result hydrateResult = this.HydrateRootGitAttributes(gitObjects, gitRepo, branch);
            if (!hydrateResult.Success)
            {
                return hydrateResult;
            }

            this.CreateGitScript();

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
                    this.tracer.RelatedError(error);
                    return new CloneVerb.Result(error);
                }
            }

            GitProcess.Result updateIndexresult = git.UpdateIndexVersion4();
            if (updateIndexresult.HasErrors)
            {
                string error = "Could not update index, error: " + updateIndexresult.Errors;
                this.tracer.RelatedError(error);
                return new CloneVerb.Result(error);
            }

            string installHooksError;
            if (!HooksInstaller.InstallHooks(this.enlistment, out installHooksError))
            {
                this.tracer.RelatedError(installHooksError);
                return new CloneVerb.Result(installHooksError);
            }
            
            if (!RepoMetadata.TryInitialize(this.tracer, this.enlistment.DotGVFSRoot, out errorMessage))
            {
                this.tracer.RelatedError(errorMessage);
                return new CloneVerb.Result(errorMessage);
            }

            try
            {
                RepoMetadata.Instance.SaveCurrentDiskLayoutVersion();
            }
            catch (Exception e)
            {
                this.tracer.RelatedError(e.ToString());
                return new CloneVerb.Result(e.Message);
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            // Prepare the working directory folder for GVFS last to ensure that gvfs mount will fail if gvfs clone has failed
            string prepGVFltError;           
            if (!GVFltCallbacks.TryPrepareFolderForGVFltCallbacks(this.enlistment.WorkingDirectoryRoot, out prepGVFltError))
            {
                this.tracer.RelatedError(prepGVFltError);
                return new CloneVerb.Result(prepGVFltError);
            }

            return new CloneVerb.Result(true);
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

        private CloneVerb.Result HydrateRootGitAttributes(GVFSGitObjects gitObjects, GitRepo repo, string branch)
        {
            List<DiffTreeResult> rootEntries = new List<DiffTreeResult>();
            GitProcess git = new GitProcess(this.enlistment);
            GitProcess.Result result = git.LsTree(
                GVFSConstants.DotGit.HeadName,
                line => rootEntries.Add(DiffTreeResult.ParseFromLsTreeLine(line, repoRoot: string.Empty)),
                recursive: false);

            if (result.HasErrors)
            {
                return new CloneVerb.Result("Error returned from ls-tree to find " + GVFSConstants.SpecialGitFiles.GitAttributes + " file: " + result.Errors);
            }

            DiffTreeResult gitAttributes = rootEntries.FirstOrDefault(entry => entry.TargetFilename.Equals(GVFSConstants.SpecialGitFiles.GitAttributes));
            if (gitAttributes == null)
            {
                return new CloneVerb.Result("This branch does not contain a " + GVFSConstants.SpecialGitFiles.GitAttributes + " file in the root folder.  This file is required by GVFS clone");
            }

            if (!repo.ObjectExists(gitAttributes.TargetSha))
            {
                if (gitObjects.TryDownloadAndSaveObject(gitAttributes.TargetSha) != GitObjects.DownloadAndSaveObjectResult.Success)
                {
                    return new CloneVerb.Result("Could not download " + GVFSConstants.SpecialGitFiles.GitAttributes + " file");
                }
            }

            return new CloneVerb.Result(true);
        }

        private void CreateGitScript()
        {
            FileInfo gitCmd = new FileInfo(Path.Combine(this.enlistment.EnlistmentRoot, "git.cmd"));
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

        private CloneVerb.Result TryInitRepo(GitRefs refs, Enlistment enlistmentToInit)
        {
            string repoPath = enlistmentToInit.WorkingDirectoryRoot;
            GitProcess.Result initResult = GitProcess.Init(enlistmentToInit);
            if (initResult.HasErrors)
            {
                string error = string.Format("Could not init repo at to {0}: {1}", repoPath, initResult.Errors);
                this.tracer.RelatedError(error);
                return new CloneVerb.Result(error);
            }

            GitProcess.Result remoteAddResult = new GitProcess(enlistmentToInit).RemoteAdd("origin", enlistmentToInit.RepoUrl);
            if (remoteAddResult.HasErrors)
            {
                string error = string.Format("Could not add remote to {0}: {1}", repoPath, remoteAddResult.Errors);
                this.tracer.RelatedError(error);
                return new CloneVerb.Result(error);
            }

            File.WriteAllText(
                Path.Combine(repoPath, GVFSConstants.DotGit.PackedRefs),
                refs.ToPackedRefs());

            return new CloneVerb.Result(true);
        }
    }
}
