using System;

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
