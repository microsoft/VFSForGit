namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// Multipliers for allocating the various pools while parsing the index.
        /// These numbers come from looking at the allocations needed for various repos
        /// </summary>
        private static class PoolAllocationMultipliers
        {
            public const double FolderDataPool = 0.17;
            public const double FileDataPool = 1.1;
            public const double StringPool = 2.4;
            public const double BytePool = 30;
            public const double ExpandPoolNewObjects = 0.15;

            // Keep 10% extra objects so we don't have to expand on the very next GetNew() call
            public const double ShrinkExtraObjects = 1.1;

            // Make sure that the shrink will reclaim at least 10% of the objects
            public const double ShrinkMinPoolSize = 0.9;
        }
    }
}
