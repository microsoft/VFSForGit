#include "PerformanceTracing.hpp"
#include <sys/types.h>
#include <stdatomic.h>
#include <IOKit/IOUserClient.h>

#if PRJFS_PERFORMANCE_TRACING_ENABLE
uint64_t PerfTracer::s_numTracers = 0;
#endif

static PrjFSPerfCounterResult s_perfCounterResults[PrjFSPerfCounter_Count];

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
    if (arguments->structureOutput == nullptr || arguments->structureOutputSize != sizeof(s_perfCounterResults))
    {
        return kIOReturnBadArgument;
    }
    
    memcpy(arguments->structureOutput, s_perfCounterResults, sizeof(s_perfCounterResults));
    return kIOReturnSuccess;
#else
    return kIOReturnUnsupported;
#endif
}

void PerfTracing_RecordSample(PrjFSPerfCounter counter, uint64_t startTime, uint64_t endTime)
{
    PrjFSPerfCounterResult* result = &s_perfCounterResults[counter];
    
    uint64_t interval = endTime - startTime;
    
    atomic_fetch_add(&result->numSamples, 1);
    
    if (0 != interval)
    {
        atomic_fetch_add(&result->sum, interval);

        __uint128_t intervalSquared = static_cast<__uint128_t>(interval) * interval;
        atomic_fetch_add(&result->sumSquares, intervalSquared);
        
        // Update minimum sample if necessary
        {
            uint64_t oldMin = atomic_load(&result->min);
            while (interval < oldMin && !atomic_compare_exchange_weak(&result->min, &oldMin, interval))
            {}
        }

        // Update maximum sample if necessary
        {
            uint64_t oldMax = atomic_load(&result->max);
            while (interval > oldMax && !atomic_compare_exchange_weak(&result->max, &oldMax, interval))
            {}
        }
    }
}

void InitProbe(PrjFSPerfCounter counter)
{
    s_perfCounterResults[counter] = PrjFSPerfCounterResult{ .min = UINT64_MAX };
}
