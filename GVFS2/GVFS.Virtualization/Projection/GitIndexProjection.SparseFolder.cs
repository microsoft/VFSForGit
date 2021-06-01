using GVFS.Common;
using System;
using System.Collections.Generic;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// This class is used to represent what has been added to sparse set of folders
        /// It is build in the RefreshSparseFolders method in the GitIndexProjection
        /// The last folder in the sparse entry will be marked with IsRecursive = true
        /// This is ONLY what is in the sparse folder set and NOT what is on disk or in
        /// the index for a folder
        /// </summary>
        /// <example>
        /// For sparse folder entries of:
        /// foo/example
        /// other
        ///
        /// The SparseFolderData would be:
        /// root
        /// Children:
        /// |- foo (IsRecursive = false, Depth = 0)
        /// |  Children:
        /// |  |- example (IsRecursive = true, Depth = 1)
        /// |
        /// |- other (IsRecursive = true, Depth = 0)
        /// </example>
        internal class SparseFolderData
        {
            public SparseFolderData()
            {
                this.Children = new Dictionary<string, SparseFolderData>(GVFSPlatform.Instance.Constants.PathComparer);
            }

            public bool IsRecursive { get; set; }
            public int Depth { get; set; }
            public Dictionary<string, SparseFolderData> Children { get; }
        }
    }
}
