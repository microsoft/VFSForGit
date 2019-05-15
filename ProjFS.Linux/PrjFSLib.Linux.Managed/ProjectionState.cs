using System;

namespace PrjFSLib.Linux
{
    public enum ProjectionState
    {
        Invalid             = 0x00000000,
        Unknown,

        Empty,
        Hydrated,
        Full
    }
}
