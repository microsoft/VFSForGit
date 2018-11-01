#ifndef PrjFSPerfCounter_h
#define PrjFSPerfCounter_h

enum PrjFSPerfCounter : int32_t
{
    // Note: ensure that any changes to this list are reflected in the PerfCounterNames array of strings
    
    PrjFSPerfCounter_VnodeOp,
    PrjFSPerfCounter_VnodeOp_GetPath,
    
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
    PrjFSPerfCounter_TryGetVirtualizationRoot_CompareProviderPid,
    PrjFSPerfCounter_TryGetVirtualizationRoot_OriginatedByProvider,
    
    PrjFSPerfCounter_VnodeOp_PreDelete,
    PrjFSPerfCounter_VnodeOp_EnumerateDirectory,
    PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory,
    PrjFSPerfCounter_VnodeOp_HydrateFile,
    
    PrjFSPerfCounter_FileOp,

    PrjFSPerfCounter_ShouldHandleFileOp,
    PrjFSPerfCounter_ShouldHandleFileOp_FindVirtualizationRoot,
    PrjFSPerfCounter_ShouldHandleFileOp_NoRootFound,
    PrjFSPerfCounter_ShouldHandleFileOp_CompareProviderPid,
    PrjFSPerfCounter_ShouldHandleFileOp_OriginatedByProvider,
    
    PrjFSPerfCounter_FileOp_Renamed,
    PrjFSPerfCounter_FileOp_HardLinkCreated,
    PrjFSPerfCounter_FileOp_FileModified,
    PrjFSPerfCounter_FileOp_FileCreated,

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
