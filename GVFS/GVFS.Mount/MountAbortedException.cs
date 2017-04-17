using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Mount
{
    public class MountAbortedException : Exception
    {
        public MountAbortedException(InProcessMountVerb verb)
        {
            this.Verb = verb;
        }

        public InProcessMountVerb Verb { get; }
    }
}
