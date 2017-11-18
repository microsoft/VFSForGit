using System;

namespace RGFS.Mount
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
