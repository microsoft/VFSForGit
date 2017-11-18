namespace RGFS.Common.Tracing
{
    public enum Keywords : long
    {
        None = 1 << 0,
        Network = 1 << 1,
        DEPRECATED_DOTGIT_FS = 1 << 2,
        Any = ~0,

        Telemetry = 1 << 3,

        NoAsimovSampling = 0x0000800000000000
    }
}