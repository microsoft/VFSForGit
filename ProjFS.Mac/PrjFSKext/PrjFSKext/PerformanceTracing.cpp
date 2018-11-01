#include "PerformanceTracing.hpp"
#include <sys/types.h>
#include <stdatomic.h>
#include <IOKit/IOUserClient.h>

#if PRJFS_PERFORMANCE_TRACING_ENABLE
uint64_t PerfTracer::s_numTracers = 0;
#endif

static PerfTracingProbe profile_probes[PrjFSPerfCounter_Count];

void InitProbe(PrjFSPerfCounter counter);

void PerfTracing_Init()
{
    for (size_t i = 0; i < PrjFSPerfCounter_Count; ++i)
    {
        InitProbe((PrjFSPerfCounter)i);
    }
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

void PerfTracing_RecordSample(PrjFSPerfCounter counter, uint64_t startTime, uint64_t endTime)
{
    PerfTracingProbe* probe = &profile_probes[counter];
    
    uint64_t interval = endTime - startTime;
    
    atomic_fetch_add(&probe->numSamples, 1);
    
    if (0 != interval)
    {
        atomic_fetch_add(&probe->sum, interval);

        __uint128_t intervalSquared = interval * interval;
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
    }
}

void InitProbe(PrjFSPerfCounter counter)
{
    profile_probes[counter] = PerfTracingProbe{ .min = UINT64_MAX };
}
