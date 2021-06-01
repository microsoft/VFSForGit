namespace GVFS.Common.Tracing
{
    public enum Keywords : long
    {
        None = 1 << 0,
        Network = 1 << 1,
        DEPRECATED = 1 << 2,
        Telemetry = 1 << 3,

        Any = ~0,
    }
}