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
    PrjFSPerfCounter_ShouldHandleVnodeOp_FindVirtualizationRoot,
    PrjFSPerfCounter_ShouldHandleVnodeOp_TemporaryDirectory,
    PrjFSPerfCounter_ShouldHandleVnodeOp_NoRootFound,
    PrjFSPerfCounter_ShouldHandleVnodeOp_ProviderOffline,
    PrjFSPerfCounter_ShouldHandleVnodeOp_OriginatedByProvider,
    PrjFSPerfCounter_ShouldHandleVnodeOp_IsHandledEvent,
    
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
