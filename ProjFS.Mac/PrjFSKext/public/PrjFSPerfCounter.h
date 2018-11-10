#ifndef PrjFSPerfCounter_h
#define PrjFSPerfCounter_h

enum PrjFSPerfCounter : int32_t
{
    // Note: ensure that any changes to this list are reflected in the PerfCounterNames array of strings
    
    PrjFSPerfCounter_VnodeOp,
        PrjFSPerfCounter_VnodeOp_GetPath,
        PrjFSPerfCounter_VnodeOp_ShouldHandle,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_IsAllowedFileSystem,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_ShouldIgnoreVnodeType,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeType,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_ReadFileFlags,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_NotInAnyRoot,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_CheckFileSystemCrawler,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_DeniedFileSystemCrawler,
        PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot,
            PrjFSPerfCounter_VnodeOp_FindRoot,
                PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_TemporaryDirectory,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_NoRootFound,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_ProviderOffline,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_CompareProviderPid,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_OriginatedByProvider,
        PrjFSPerfCounter_VnodeOp_PreDelete,
        PrjFSPerfCounter_VnodeOp_EnumerateDirectory,
        PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory,
        PrjFSPerfCounter_VnodeOp_HydrateFile,
    
    PrjFSPerfCounter_FileOp,
        PrjFSPerfCounter_FileOp_ShouldHandle,
            PrjFSPerfCounter_FileOp_ShouldHandle_FindVirtualizationRoot,
                PrjFSPerfCounter_FileOp_FindRoot,
                    PrjFSPerfCounter_FileOp_FindRoot_Iteration,
                PrjFSPerfCounter_FileOp_ShouldHandle_NoRootFound,
            PrjFSPerfCounter_FileOp_ShouldHandle_CompareProviderPid,
                PrjFSPerfCounter_FileOp_ShouldHandle_OriginatedByProvider,
        PrjFSPerfCounter_FileOp_Renamed,
        PrjFSPerfCounter_FileOp_HardLinkCreated,
        PrjFSPerfCounter_FileOp_FileModified,
        PrjFSPerfCounter_FileOp_FileCreated,

    PrjFSPerfCounter_Count,
};

struct PrjFSPerfCounterResult
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
