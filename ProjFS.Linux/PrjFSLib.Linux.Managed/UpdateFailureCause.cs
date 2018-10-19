using System;

namespace PrjFSLib.Linux
{
    [Flags]
    public enum UpdateFailureCause
    {
        NoFailure   = 0x00000000,
        DirtyData   = 0x00000002,
        ReadOnly    = 0x00000008,
    }
}
