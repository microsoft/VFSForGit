#include "kext-perf-tracing.hpp"
#include "../../PrjFSKext/public/PrjFSCommon.h"
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

static const char* const PerfCounterNames[Probe_Count] =
{
    [Probe_VnodeOp] = "VnodeOp",
    [Probe_FileOp] = "FileOp",
    [Probe_Op_EarlyOut] = "Op_EarlyOut",
    [Probe_Op_NoVirtualizationRootFlag] = "Op_NoVirtualizationRootFlag",
    [Probe_Op_EmptyFlag] = "Op_EmptyFlag",
    [Probe_Op_DenyCrawler] = "Op_DenyCrawler",
    [Probe_Op_Offline] = "Op_Offline",
    [Probe_Op_Provider] = "Op_Provider",
    [Probe_VnodeOp_PopulatePlaceholderDirectory] = "VnodeOp_PopulatePlaceholderDirectory",
    [Probe_VnodeOp_HydratePlaceholderFile] = "VnodeOp_HydratePlaceholderFile",
    
    [Probe_Op_IdentifySplit] = "Op_IdentifySplit",
    [Probe_Op_VirtualizationRootFindSplit] = "Op_VirtualizationRootFindSplit",
    
    [Probe_ReadFileFlags] = "Probe_ReadFileFlags",
    [Probe_VirtualizationRoot_Find] = "VirtualizationRoot_Find",
    [Probe_VirtualizationRoot_FindIteration] = "VirtualizationRoot_FindIteration",
};

bool PrjFSLog_FetchAndPrintKextProfilingData(io_connect_t connection)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        mach_timebase_info(&s_machTimebase);
    });
    
    PerfTracingProbe probes[Probe_Count];
    size_t out_size = sizeof(probes);
    IOReturn ret = IOConnectCallStructMethod(connection, LogSelector_FetchProfilingData, nullptr, 0, probes, &out_size);
    if (ret == kIOReturnUnsupported)
    {
        return false;
    }
    else if (ret == kIOReturnSuccess)
    {
        for (unsigned i = 0; i < Probe_Count; ++i)
        {
            double samples = probes[i].numSamples1;
            double sum_abs = probes[i].sum;
            double stddev_abs = samples > 1 ? sqrt((samples * probes[i].sumSquares - sum_abs * sum_abs) / (samples * (samples - 1))) : 0.0;

            double sum_ns = nanosecondsFromAbsoluteTime(sum_abs);
            double stddev_ns = nanosecondsFromAbsoluteTime(stddev_abs);
            double mean_ns = samples > 0 ? sum_ns / samples : 0;
            printf("%2u %40s  %8llu [%8llu] samples, total time: %15.0f ns, mean: %10.2f ns +/- %11.2f",
                i, PerfCounterNames[i], probes[i].numSamples1, probes[i].numSamples2, sum_ns, mean_ns, stddev_ns);
            if (probes[i].min != UINT64_MAX)
            {
                printf(", min: %7llu ns, max: %10llu ns\n",  nanosecondsFromAbsoluteTime(probes[i].min), nanosecondsFromAbsoluteTime(probes[i].max));
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
    fflush(stdout);
    return true;
}
