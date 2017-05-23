using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using GVFS.GVFlt;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            GitObjects gitObjects = new GitObjects(this.tracer, this.enlistment, this.objectRequestor);

            CloneVerb.Result initRepoResult = this.TryInitRepo(refs, this.enlistment);
            if (!initRepoResult.Success)
            {
                return initRepoResult;
            }

            if (!gitObjects.TryDownloadAndSaveCommits(refs.GetTipCommitIds(), commitDepth: 2))
            {
                return new CloneVerb.Result("Could not download tip commits from: " + Uri.EscapeUriString(this.enlistment.ObjectsEndpointUrl));
            }

            GitProcess git = new GitProcess(this.enlistment);
            if (!this.SetConfigSettings(git))
            {
                return new CloneVerb.Result("Unable to configure git repo");
            }

            git.CreateBranchWithUpstream(branch, "origin/" + branch);

            File.WriteAllText(
                Path.Combine(this.enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Head),
                "ref: refs/heads/" + branch);

            File.AppendAllText(
                Path.Combine(this.enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath),
                GVFSConstants.GitPathSeparatorString + GVFSConstants.SpecialGitFiles.GitAttributes + "\n");

            try
            {
                CloneVerb.Result hydrateResult = this.HydrateRootGitAttributes_CanTimeout(gitObjects, branch);
                if (!hydrateResult.Success)
                {
                    return hydrateResult;
                }
            }
            catch (TimeoutException)
            {
                return new CloneVerb.Result("Failed to hydrate root .gitattributes file");
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
            if (!HooksInstallHelper.InstallHooks(this.enlistment, out installHooksError))
            {
                this.tracer.RelatedError(installHooksError);
                return new CloneVerb.Result(installHooksError);
            }

            using (RepoMetadata repoMetadata = new RepoMetadata(this.enlistment.DotGVFSRoot))
            {
                repoMetadata.SaveCurrentDiskLayoutVersion();
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

        private bool SetConfigSettings(GitProcess git)
        {
            return this.enlistment.TrySetCacheServerUrlConfig() &&
                GVFSVerb.TrySetGitConfigSettings(git);
        }

        private CloneVerb.Result HydrateRootGitAttributes_CanTimeout(GitObjects gitObjects, string branch)
        {
            using (GitCatFileBatchProcess catFile = new GitCatFileBatchProcess(this.tracer, this.enlistment))
            {
                string treeSha = catFile.GetTreeSha_CanTimeout(branch);
                GitTreeEntry gitAttributes = catFile.GetTreeEntries_CanTimeout(treeSha).FirstOrDefault(entry => entry.Name.Equals(GVFSConstants.SpecialGitFiles.GitAttributes));

                if (gitAttributes == null)
                {
                    return new CloneVerb.Result("This branch does not contain a " + GVFSConstants.SpecialGitFiles.GitAttributes + " file in the root folder.  This file is required by GVFS clone");
                }

                if (!gitObjects.TryDownloadAndSaveBlobs(new[] { gitAttributes.Sha }))
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
