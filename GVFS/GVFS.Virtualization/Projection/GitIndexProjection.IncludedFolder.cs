using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// This class is used to represent what has been added to included set of folders
        /// It is build in the RefreshFoldersToInclude method in the GitIndexProjection
        /// The last folder in the include entry will be marked with IsRecursive = true
        /// The is ONLY what is in the included folder set and NOT what is on disk or in
        /// the index for a folder
        /// </summary>
        /// <example>
        /// For included folder entries of:
        /// GVFS/example
        /// other
        ///
        /// The IncludedFolderData would be:
        /// root
        /// Children:
        /// |- GVFS (IsRecursive = false, Depth = 0)
        /// |  Children:
        /// |  |- example (IsRecursive = true, Depth = 1)
        /// |
        /// |- other (IsRecursive = true, Depth = 0)
        /// </example>
        internal class IncludedFolderData
        {
            public IncludedFolderData()
            {
                this.Children = new Dictionary<string, IncludedFolderData>(StringComparer.OrdinalIgnoreCase);
            }

            public bool IsRecursive { get; set; }
            public int Depth { get; set; }
            public Dictionary<string, IncludedFolderData> Children { get; }
        }
    }
}
