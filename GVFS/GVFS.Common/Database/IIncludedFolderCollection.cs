using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.Database
{
    public interface IIncludedFolderCollection
    {
        List<string> GetAll();

        void Add(string directory);
        void Remove(string directory);
    }
}
