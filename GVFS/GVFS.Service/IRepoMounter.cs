namespace GVFS.Service
{
    public interface IRepoMounter
    {
        bool MountRepository(string repoRoot, int sessionId);
    }
}
