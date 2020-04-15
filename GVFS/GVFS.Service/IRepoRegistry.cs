using System.Collections.Generic;

namespace GVFS.Service
{
    public interface IRepoRegistry
    {
        bool TryRegisterRepo(string repoRoot, string ownerSID, out string errorMessage);
        bool TryDeactivateRepo(string repoRoot, out string errorMessage);
        bool TryGetActiveRepos(out List<RepoRegistration> repoList, out string errorMessage);
        bool TryRemoveRepo(string repoRoot, out string errorMessage);
        void AutoMountRepos(string userId, int sessionId, bool checkDirectoryExists = true);
        void TraceStatus();
    }
}
