using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Service.Handlers
{
    public class GetActiveRepoListHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.GetActiveRepoListRequest request;
        private ITracer tracer;
        private IRepoRegistry registry;

        public GetActiveRepoListHandler(
            ITracer tracer,
            IRepoRegistry registry,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.GetActiveRepoListRequest request)
        {
            this.tracer = tracer;
            this.registry = registry;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            string errorMessage;
            NamedPipeMessages.GetActiveRepoListRequest.Response response = new NamedPipeMessages.GetActiveRepoListRequest.Response();
            response.State = NamedPipeMessages.CompletionState.Success;
            response.RepoList = new List<string>();

            List<RepoRegistration> repos;
            if (this.registry.TryGetActiveRepos(out repos, out errorMessage))
            {
                List<string> tempRepoList = repos.Select(repo => repo.EnlistmentRoot).ToList();

                foreach (string repoRoot in tempRepoList)
                {
                    if (!this.IsValidRepo(repoRoot))
                    {
                        if (!this.registry.TryRemoveRepo(repoRoot, out errorMessage))
                        {
                            this.tracer.RelatedInfo("Removing an invalid repo failed with error: " + response.ErrorMessage);
                        }
                        else
                        {
                            this.tracer.RelatedInfo("Removed invalid repo entry from registry: " + repoRoot);
                        }
                    }
                    else
                    {
                        response.RepoList.Add(repoRoot);
                    }
                }
            }
            else
            {
                response.ErrorMessage = errorMessage;
                response.State = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Get active repo list failed with error: " + response.ErrorMessage);
            }

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }

        private bool IsValidRepo(string repoRoot)
        {
            if (!Directory.Exists(repoRoot))
            {
                return false;
            }

            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

            string hooksVersion = null;
            string error = null;
            if (GVFSPlatform.Instance.TryGetGVFSHooksVersion(out hooksVersion, out error))
            {
                try
                {
                    GVFSEnlistment enlistment = GVFSEnlistment.CreateFromDirectory(
                        repoRoot,
                        gitBinPath,
                        authentication: null);
                }
                catch (InvalidRepoException e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add(nameof(repoRoot), repoRoot);
                    metadata.Add(nameof(gitBinPath), gitBinPath);
                    metadata.Add("Exception", e.ToString());
                    this.tracer.RelatedInfo(metadata, $"{nameof(this.IsValidRepo)}: Found invalid repo");

                    return false;
                }
            }
            else
            {
                this.tracer.RelatedError($"{nameof(this.IsValidRepo)}: {nameof(GVFSPlatform.Instance.TryGetGVFSHooksVersion)} failed. {error}");
                return false;
            }

            return true;
        }
    }
}
