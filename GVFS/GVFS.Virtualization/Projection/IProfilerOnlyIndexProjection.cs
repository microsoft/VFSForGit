using GVFS.Common.Tracing;

namespace GVFS.Virtualization.Projection
{
    /// <summary>
    /// Interface used for performace profiling GitIndexProjection.  This interface
    /// allows performance tests to force GitIndexProjection to parse the index on demand so
    /// that index parsing can be measured and profiled.
    /// </summary>
    public interface IProfilerOnlyIndexProjection
    {
        void ForceRebuildProjection();
        void ForceAddMissingModifiedPaths(ITracer tracer);
    }
}
