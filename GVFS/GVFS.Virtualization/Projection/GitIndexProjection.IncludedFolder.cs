using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal class IncludedFolderData
        {
            public IncludedFolderData()
            {
                this.Children = new Dictionary<string, IncludedFolderData>();
            }

            public bool IsRecursive { get; set; }
            public Dictionary<string, IncludedFolderData> Children { get; }
        }
    }
}
