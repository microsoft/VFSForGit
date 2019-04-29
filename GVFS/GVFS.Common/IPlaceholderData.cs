using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common
{
    public interface IPlaceholderData
    {
        string Path { get; }
        string Sha { get; }
        bool IsFolder { get; }
        bool IsExpandedFolder { get; }
        bool IsPossibleTombstoneFolder { get; }
    }
}
