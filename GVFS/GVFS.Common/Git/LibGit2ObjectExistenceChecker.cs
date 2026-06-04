using GVFS.Common.Tracing;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Object existence checker backed by libgit2 — one instance per worker thread.
    /// </summary>
    public class LibGit2ObjectExistenceChecker : IObjectExistenceChecker
    {
        private readonly LibGit2Repo repo;

        public LibGit2ObjectExistenceChecker(ITracer tracer, string repoPath)
        {
            this.repo = new LibGit2Repo(tracer, repoPath);
        }

        public bool ObjectExists(string sha)
        {
            return this.repo.ObjectExists(sha);
        }

        public void Dispose()
        {
            this.repo.Dispose();
        }
    }
}
