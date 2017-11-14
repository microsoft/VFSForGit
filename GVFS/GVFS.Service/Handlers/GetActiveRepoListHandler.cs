using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Service.Handlers
{
    public class GetActiveRepoListHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.GetActiveRepoListRequest request;
        private ITracer tracer;
        private RepoRegistry registry;

        public GetActiveRepoListHandler(
            ITracer tracer,
            RepoRegistry registry,
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

            List<RepoRegistration> repos;
            if (this.registry.TryGetActiveRepos(out repos, out errorMessage))
            {
                response.RepoList = repos.Select(repo => repo.EnlistmentRoot).ToList();
                response.State = NamedPipeMessages.CompletionState.Success;
            }
            else
            {
                response.ErrorMessage = errorMessage;
                response.State = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Get active repo list failed with error: " + response.ErrorMessage);
            }

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
