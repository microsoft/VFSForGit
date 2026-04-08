using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    public class LibGit2Exception : Exception
    {
        public LibGit2Exception(string message) : base(message)
        {
        }

        public LibGit2Exception(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
