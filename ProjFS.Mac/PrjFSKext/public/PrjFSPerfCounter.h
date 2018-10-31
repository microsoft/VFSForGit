#ifndef PrjFSPerfCounter_h
#define PrjFSPerfCounter_h

enum PrjFSPerfCounter : int32_t
{
    // Note: ensure that any changes to this list are reflected in the PerfCounterNames array of strings
    
    PrjFSPerfCounter_VnodeOp,
    
    PrjFSPerfCounter_ShouldHandleVnodeOp,
    PrjFSPerfCounter_ShouldHandleVnodeOp_IsAllowedFileSystem,
    PrjFSPerfCounter_ShouldHandleVnodeOp_ShouldIgnoreVnodeType,
    PrjFSPerfCounter_ShouldHandleVnodeOp_IgnoredVnodeType,
    PrjFSPerfCounter_ShouldHandleVnodeOp_ReadFileFlags,
    PrjFSPerfCounter_ShouldHandleVnodeOp_NotInAnyRoot,
    PrjFSPerfCounter_ShouldHandleVnodeOp_CheckFileSystemCrawler,
    PrjFSPerfCounter_ShouldHandleVnodeOp_DeniedFileSystemCrawler,
    
    PrjFSPerfCounter_TryGetVirtualizationRoot,
    PrjFSPerfCounter_TryGetVirtualizationRoot_TemporaryDirectory,
    PrjFSPerfCounter_TryGetVirtualizationRoot_NoRootFound,
    PrjFSPerfCounter_TryGetVirtualizationRoot_ProviderOffline,
    PrjFSPerfCounter_TryGetVirtualizationRoot_OriginatedByProvider,
    
    PrjFSPerfCounter_VnodeOp_PreDelete,
    PrjFSPerfCounter_VnodeOp_EnumerateDirectory,
    PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory,
    PrjFSPerfCounter_VnodeOp_HydrateFile,
    
    PrjFSPerfCounter_Count,
};

struct PerfTracingProbe
{
    _Atomic uint64_t numSamples;
    
    // Units: Mach absolute time (squared for sumSquares)
    // Sum of measured sample intervals
    _Atomic uint64_t sum;
    // Smallest encountered interval
    _Atomic uint64_t min;
    // Largest encountered interval
    _Atomic uint64_t max;
    // Sum-of-squares of measured time intervals (for stddev)
    _Atomic __uint128_t sumSquares;
};

#endif /* PrjFSPerfCounter_h */
