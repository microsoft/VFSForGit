#include "kext-perf-tracing.hpp"
#include "../../PrjFSKext/public/PrjFSCommon.h"
#include "../../PrjFSKext/public/PrjFSPerfCounter.h"
#include "../../PrjFSKext/public/PrjFSLogClientShared.h"
#include <mach/mach_time.h>
#include <dispatch/dispatch.h>
#include <IOKit/IOKitLib.h>
#include <cstdio>
#include <cmath>

static mach_timebase_info_data_t s_machTimebase;

static uint64_t nanosecondsFromAbsoluteTime(uint64_t machAbsoluteTime)
{
    return static_cast<__uint128_t>(machAbsoluteTime) * s_machTimebase.numer / s_machTimebase.denom;
}

static const char* const PerfCounterNames[PrjFSPerfCounter_Count] =
{
    [PrjFSPerfCounter_VnodeOp]                                              = "HandleVnodeOperation",
    [PrjFSPerfCounter_VnodeOp_GetPath]                                      = " |--GetPath",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle]                                 = " |--ShouldHandleVnodeOpEvent",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_IsAllowedFileSystem]             = " |  |--VnodeIsOnAllowedFilesystem",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_ShouldIgnoreVnodeType]           = " |  |--ShouldIgnoreVnodeType",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeType]                = " |  |  |--Ignored",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_ReadFileFlags]                   = " |  |--TryReadVNodeFileFlags",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_NotInAnyRoot]                    = " |  |  |--NotInAnyRoot",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_CheckFileSystemCrawler]          = " |  |--IsFileSystemCrawler",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_DeniedFileSystemCrawler]         = " |     |--Denied",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot]                        = " |--TryGetVirtualizationRoot",
    [PrjFSPerfCounter_VnodeOp_FindRoot]                                     = " |  |--FindForVnode",
    [PrjFSPerfCounter_VnodeOp_FindRoot_Iteration]                           = " |  |  |--inner_loop_iterations",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_TemporaryDirectory]     = " |  |--TemporaryDirectory",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_NoRootFound]            = " |  |--NoRootFound",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_ProviderOffline]        = " |  |--ProviderOffline",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_CompareProviderPid]     = " |  |--CompareProviderPid",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_OriginatedByProvider]   = " |     |--OriginatedByProvider",
    [PrjFSPerfCounter_VnodeOp_PreDelete]                                    = " |--RaisePreDeleteEvent",
    [PrjFSPerfCounter_VnodeOp_EnumerateDirectory]                           = " |--RaiseEnumerateDirectoryEvent",
    [PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory]                = " |--RaiseRecursivelyEnumerateEvent",
    [PrjFSPerfCounter_VnodeOp_HydrateFile]                                  = " |--RaiseHydrateFileEvent",
    [PrjFSPerfCounter_FileOp]                                               = "HandleFileOpOperation",
    [PrjFSPerfCounter_FileOp_ShouldHandle]                                  = " |--ShouldHandleFileOpEvent",
    [PrjFSPerfCounter_FileOp_ShouldHandle_FindVirtualizationRoot]           = " |  |--FindVirtualizationRoot",
    [PrjFSPerfCounter_FileOp_FindRoot]                                      = " |  |  |--FindForVnode",
    [PrjFSPerfCounter_FileOp_FindRoot_Iteration]                            = " |  |  |  |--inner_loop_iterations",
    [PrjFSPerfCounter_FileOp_ShouldHandle_NoRootFound]                      = " |  |  |--NoRootFound",
    [PrjFSPerfCounter_FileOp_ShouldHandle_CompareProviderPid]               = " |  |--CompareProviderPid",
    [PrjFSPerfCounter_FileOp_ShouldHandle_OriginatedByProvider]             = " |     |--OriginatedByProvider",
    [PrjFSPerfCounter_FileOp_Renamed]                                       = " |--RaiseRenamedEvent",
    [PrjFSPerfCounter_FileOp_HardLinkCreated]                               = " |--RaiseHardLinkCreatedEvent",
    [PrjFSPerfCounter_FileOp_FileModified]                                  = " |--RaiseFileModifiedEvent",
    [PrjFSPerfCounter_FileOp_FileCreated]                                   = " |--RaiseFileCreatedEvent",
};

bool PrjFSLog_FetchAndPrintKextProfilingData(io_connect_t connection)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        mach_timebase_info(&s_machTimebase);
    });
    
    PrjFSPerfCounterResult counters[PrjFSPerfCounter_Count];
    size_t out_size = sizeof(counters);
    IOReturn ret = IOConnectCallStructMethod(connection, LogSelector_FetchProfilingData, nullptr, 0, counters, &out_size);
    if (ret == kIOReturnUnsupported)
    {
        return false;
    }
    else if (ret == kIOReturnSuccess)
    {
        printf("   Counter                             [ Samples  ][Total time (ns)][Mean (ns)   ][Stddev (ns) ][Min (ns)][Max (ns)  ]\n");
        printf("----------------------------------------------------------------------------------------------------------------------\n");
        
        for (unsigned i = 0; i < PrjFSPerfCounter_Count; ++i)
        {
            double numSamples = counters[i].numSamples;
            printf(
                "%2u %-35s [%10llu]",
                i,
                PerfCounterNames[i],
                counters[i].numSamples);
            
            if (counters[i].min != UINT64_MAX)
            {
                // The values on the counter are reported in units of mach absolute time
                double sum = counters[i].sum;
                double stddev = numSamples > 1 ? sqrt((numSamples * counters[i].sumSquares - sum * sum) / (numSamples * (numSamples - 1))) : 0.0;

                uint64_t sumNS = nanosecondsFromAbsoluteTime(sum);
                uint64_t meanNS = numSamples > 0 ? sumNS / numSamples : 0;

                printf(
                    "[%15llu][%12llu][%12llu][%8llu][%10llu]\n",
                    sumNS,
                    meanNS,
                    nanosecondsFromAbsoluteTime(stddev),
                    nanosecondsFromAbsoluteTime(counters[i].min),
                    nanosecondsFromAbsoluteTime(counters[i].max));
            }
            else
            {
                printf("\n");
            }
        }
    }
    else
    {
        fprintf(stderr, "fetching profiling data from kernel failed: 0x%x\n", ret);
        return false;
    }
    
    printf("\n");
    fflush(stdout);
    
    return true;
}
