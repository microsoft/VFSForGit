namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Receives the working-tree changes that the file system callbacks of the mount see.
    /// </summary>
    public interface IWorkingDirectoryChangeObserver
    {
        /// <summary>
        /// A file or folder was created, written, renamed, or deleted. This runs in a file system
        /// callback, so it must be fast, it must not wait for a Git command, and it must not throw.
        /// </summary>
        /// <param name="relativePath">The path relative to the working-tree root, with either separator.</param>
        /// <param name="isFolder">True for a folder.</param>
        void OnWorkingDirectoryPathChanged(string relativePath, bool isFolder);
    }
}
