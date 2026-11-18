using System;

namespace GVFS.Virtualization.Projection
{
    /// <summary>
    /// The projection cannot expand a sparse-directory entry of a sparse index
    /// (gvfs.auto-sparse-index is true). The message tells the user what to do next.
    /// </summary>
    /// <remarks>
    /// Do not derive from IOException. The index parsing thread retries an IOException, and
    /// a retry under the projection write lock does not fix a tree that cannot be read.
    /// </remarks>
    public class SparseIndexExpansionException : Exception
    {
        public SparseIndexExpansionException(string message)
            : base(message)
        {
        }
    }
}
