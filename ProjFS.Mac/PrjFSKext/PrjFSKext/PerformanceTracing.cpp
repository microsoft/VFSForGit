#include "PerformanceTracing.hpp"
#include <sys/types.h>
#include <stdatomic.h>
#include <IOKit/IOUserClient.h>

PerfTracingProbe profile_probes[Probe_Count];

void PerfTracing_Init()
{
    for (size_t i = 0; i < Probe_Count; ++i)
    {
        PerfTracing_ProbeInit(&profile_probes[i]);
    }
}

void PerfTracing_ProbeInit(PerfTracingProbe* probe)
{
    *probe = PerfTracingProbe{ .min = UINT64_MAX };
}

IOReturn PerfTracing_ExportDataUserClient(IOExternalMethodArguments* arguments)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    if (arguments->structureOutput == nullptr || arguments->structureOutputSize != sizeof(profile_probes))
    {
        return kIOReturnBadArgument;
    }
    
    memcpy(arguments->structureOutput, profile_probes, sizeof(profile_probes));
    return kIOReturnSuccess;
#else
    return kIOReturnUnsupported;
#endif
}

void PerfTracing_RecordSample(PerfTracingProbe* probe, uint64_t startTime, uint64_t endTime)
{
    uint64_t interval = endTime - startTime;
    
    atomic_fetch_add(&probe->numSamples1, 1);
    atomic_fetch_add(&probe->sum, interval);
    
    __uint128_t intervalSquared = interval;
    intervalSquared *= intervalSquared;
    atomic_fetch_add(&probe->sumSquares, intervalSquared);
    
    // Update minimum sample if necessary
    {
        uint64_t oldMin = atomic_load(&probe->min);
        while (interval < oldMin && !atomic_compare_exchange_weak(&probe->min, &oldMin, interval))
        {}
    }

    // Update maximum sample if necessary
    {
        uint64_t oldMax = atomic_load(&probe->max);
        while (interval > oldMax && !atomic_compare_exchange_weak(&probe->max, &oldMax, interval))
        {}
    }

    atomic_fetch_add(&probe->numSamples2, 1);
}

