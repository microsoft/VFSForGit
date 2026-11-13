using System;

namespace GVFS.Virtualization.Projection
{
    /// <summary>
    /// The git index is a sparse index (git index.sparse), and the projection cannot use it.
    /// The message tells the user how to expand the index.
    /// </summary>
    /// <remarks>
    /// Do not derive from IOException. The index parsing thread retries an IOException, and a
    /// sparse index does not change when the parse is retried.
    /// </remarks>
    public class SparseIndexNotSupportedException : Exception
    {
        public SparseIndexNotSupportedException(string message)
            : base(message)
        {
        }
    }
}
