using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    public class GitTreeEntry
    {
        public GitTreeEntry(string name, string sha, bool isTree, bool isBlob)
        {
            this.Name = name;
            this.Sha = sha;
            this.IsTree = isTree;
            this.IsBlob = isBlob;
        }

        public string Name { get; private set; }
        public string Sha { get; private set; }
        public bool IsTree { get; private set; }
        public bool IsBlob { get; private set; }

        public long Size { get; set; }
    }
}
