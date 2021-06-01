using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.Database
{
    public interface ISparseCollection
    {
        HashSet<string> GetAll();

        void Add(string directory);
        void Remove(string directory);
    }
}
