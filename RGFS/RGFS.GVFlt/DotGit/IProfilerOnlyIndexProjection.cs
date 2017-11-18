namespace RGFS.GVFlt.DotGit
{
    /// <summary>
    /// Interface used for performace profiling GitIndexProjection.  This interface
    /// allows performance tests to force GitIndexProjection to parse the index on demand so
    /// that index parsing can be measured and profiled.
    /// </summary>
    public interface IProfilerOnlyIndexProjection
    {
        void ForceRebuildProjection();

        void ForceUpdateOffsetsAndValidateSparseCheckout();

        void ForceValidateSparseCheckout();
    }
}
