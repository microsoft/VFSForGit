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
    [PrjFSPerfCounter_VnodeOp]                                          = "HandleVnodeOperation",
    [PrjFSPerfCounter_VnodeOp_GetPath]                                  = " |--GetPath",
    [PrjFSPerfCounter_ShouldHandleVnodeOp]                              = " |--ShouldHandleVnodeOpEvent",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_IsAllowedFileSystem]          = " |  |--VnodeIsOnAllowedFilesystem",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_ShouldIgnoreVnodeType]        = " |  |--ShouldIgnoreVnodeType",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_IgnoredVnodeType]             = " |  |  |--Ignored",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_ReadFileFlags]                = " |  |--TryReadVNodeFileFlags",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_NotInAnyRoot]                 = " |  |  |--NotInAnyRoot",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_CheckFileSystemCrawler]       = " |  |--IsFileSystemCrawler",
    [PrjFSPerfCounter_ShouldHandleVnodeOp_DeniedFileSystemCrawler]      = " |     |--Denied",
    [PrjFSPerfCounter_TryGetVirtualizationRoot]                         = " |--TryGetVirtualizationRoot",
    [PrjFSPerfCounter_TryGetVirtualizationRoot_TemporaryDirectory]      = " | |--TemporaryDirectory",
    [PrjFSPerfCounter_TryGetVirtualizationRoot_NoRootFound]             = " | |--NoRootFound",
    [PrjFSPerfCounter_TryGetVirtualizationRoot_ProviderOffline]         = " | |--ProviderOffline",
    [PrjFSPerfCounter_TryGetVirtualizationRoot_CompareProviderPid]      = " | |--CompareProviderPid",
    [PrjFSPerfCounter_TryGetVirtualizationRoot_OriginatedByProvider]    = " |    |--OriginatedByProvider",
    [PrjFSPerfCounter_VnodeOp_PreDelete]                                = " |--RaisePreDeleteEvent",
    [PrjFSPerfCounter_VnodeOp_EnumerateDirectory]                       = " |--RaiseEnumerateDirectoryEvent",
    [PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory]            = " |--RaiseRecursivelyEnumerateEvent",
    [PrjFSPerfCounter_VnodeOp_HydrateFile]                              = " |--RaiseHydrateFileEvent",
    [PrjFSPerfCounter_FileOp]                                           = "HandleFileOpOperation",
    [PrjFSPerfCounter_ShouldHandleFileOp]                               = " |--ShouldHandleFileOpEvent",
    [PrjFSPerfCounter_ShouldHandleFileOp_FindVirtualizationRoot]        = " |  |--FindVirtualizationRoot",
    [PrjFSPerfCounter_ShouldHandleFileOp_NoRootFound]                   = " |  |  |--NoRootFound",
    [PrjFSPerfCounter_ShouldHandleFileOp_CompareProviderPid]            = " |  |--CompareProviderPid",
    [PrjFSPerfCounter_ShouldHandleFileOp_OriginatedByProvider]          = " |     |--OriginatedByProvider",
    [PrjFSPerfCounter_FileOp_Renamed]                                   = " |--RaiseRenamedEvent",
    [PrjFSPerfCounter_FileOp_HardLinkCreated]                           = " |--RaiseHardLinkCreatedEvent",
    [PrjFSPerfCounter_FileOp_FileModified]                              = " |--RaiseFileModifiedEvent",
    [PrjFSPerfCounter_FileOp_FileCreated]                               = " |--RaiseFileCreatedEvent",
};

bool PrjFSLog_FetchAndPrintKextProfilingData(io_connect_t connection)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        mach_timebase_info(&s_machTimebase);
    });
    
    PerfTracingProbe probes[PrjFSPerfCounter_Count];
    size_t out_size = sizeof(probes);
    IOReturn ret = IOConnectCallStructMethod(connection, LogSelector_FetchProfilingData, nullptr, 0, probes, &out_size);
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
            double samples = probes[i].numSamples;
            printf(
                "%2u %-35s [%10llu]",
                i,
                PerfCounterNames[i],
                probes[i].numSamples);
            
            if (probes[i].min != UINT64_MAX)
            {
                double sum_abs = probes[i].sum;
                double stddev_abs = samples > 1 ? sqrt((samples * probes[i].sumSquares - sum_abs * sum_abs) / (samples * (samples - 1))) : 0.0;

                double sum_ns = nanosecondsFromAbsoluteTime(sum_abs);
                double stddev_ns = nanosecondsFromAbsoluteTime(stddev_abs);
                double mean_ns = samples > 0 ? sum_ns / samples : 0;

                printf(
                    "[%15.0f][%12.0f][%12.0f][%8llu][%10llu]\n",
                    sum_ns,
                    mean_ns,
                    stddev_ns,
                    nanosecondsFromAbsoluteTime(probes[i].min),
                    nanosecondsFromAbsoluteTime(probes[i].max));
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
