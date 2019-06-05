#ifndef PrjFSPerfCounter_h
#define PrjFSPerfCounter_h

enum PrjFSPerfCounter : int32_t
{
    // Note: ensure that any changes to this list are reflected in the PerfCounterNames array of strings
    
    PrjFSPerfCounter_VnodeOp,
        PrjFSPerfCounter_VnodeOp_GetPath,
        PrjFSPerfCounter_VnodeOp_BasicVnodeChecks,
        PrjFSPerfCounter_VnodeOp_ShouldHandle,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_IsVnodeAccessCheck,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeAccessCheck,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_ReadFileFlags,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_NotInAnyRoot,
            PrjFSPerfCounter_VnodeOp_ShouldHandle_CheckFileSystemCrawler,
                PrjFSPerfCounter_VnodeOp_ShouldHandle_DeniedFileSystemCrawler,
        PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot,
            PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit,
            PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss,
                PrjFSPerfCounter_VnodeOp_FindRoot,
                    PrjFSPerfCounter_VnodeOp_FindRoot_Iteration,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_TemporaryDirectory,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_NoRootFound,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_ProviderOffline,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_OriginatedByProvider,
            PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_UserRestriction,
        PrjFSPerfCounter_VnodeOp_PreDelete,
        PrjFSPerfCounter_VnodeOp_EnumerateDirectory,
        PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory,
        PrjFSPerfCounter_VnodeOp_HydrateFile,
        PrjFSPerfCounter_VnodeOp_PreConvertToFull,
    
    PrjFSPerfCounter_FileOp,
        PrjFSPerfCounter_FileOp_ShouldHandle,
            PrjFSPerfCounter_FileOp_ShouldHandle_FindVirtualizationRoot,
                PrjFSPerfCounter_FileOp_Vnode_Cache_Hit,
                PrjFSPerfCounter_FileOp_Vnode_Cache_Miss,
                    PrjFSPerfCounter_FileOp_FindRoot,
                        PrjFSPerfCounter_FileOp_FindRoot_Iteration,
                PrjFSPerfCounter_FileOp_ShouldHandle_NoRootFound,
            PrjFSPerfCounter_FileOp_ShouldHandle_FindProviderPathBased,
                PrjFSPerfCounter_FileOp_ShouldHandle_NoProviderFound,
            PrjFSPerfCounter_FileOp_ShouldHandle_CheckProvider,
                PrjFSPerfCounter_FileOp_ShouldHandle_OfflineRoot,
                PrjFSPerfCounter_FileOp_ShouldHandle_OriginatedByProvider,
        PrjFSPerfCounter_FileOp_Renamed,
        PrjFSPerfCounter_FileOp_HardLinkCreated,
        PrjFSPerfCounter_FileOp_FileModified,
        PrjFSPerfCounter_FileOp_FileCreated,

    PrjFSPerfCounter_CacheCapacity,
    PrjFSPerfCounter_CacheInvalidateCount,
    PrjFSPerfCounter_CacheFullCount,
    PrjFSPerfCounter_Count,
};

constexpr unsigned int PrjFSPerfCounterBuckets = 64;

struct PrjFSPerfCounterResult
{
    _Atomic uint64_t numSamples;
    
    // Units: Mach absolute time
    _Atomic uint64_t sum;
    _Atomic uint64_t min;
    _Atomic uint64_t max;
    
    // log-scale histogram buckets
    _Atomic uint64_t sampleBuckets[PrjFSPerfCounterBuckets];
};

#endif /* PrjFSPerfCounter_h */
