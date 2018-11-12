#include "PerformanceTracing.hpp"
#include "KextLog.hpp"
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
    // The buffer will come in either as a memory descriptor or direct pointer, depending on size
    if (nullptr != arguments->structureOutputDescriptor)
    {
        IOMemoryDescriptor* structureOutput = arguments->structureOutputDescriptor;
        if (sizeof(s_perfCounterResults) != structureOutput->getLength())
        {
            KextLog_Info("PerfTracing_ExportDataUserClient: structure output descriptor size %llu, expected %lu\n", structureOutput->getLength(), sizeof(s_perfCounterResults));
            return kIOReturnBadArgument;
        }
        
        IOReturn result = structureOutput->prepare(kIODirectionIn);
        if (kIOReturnSuccess == result)
        {
            structureOutput->writeBytes(0 /* offset */, s_perfCounterResults, sizeof(s_perfCounterResults));
            structureOutput->complete(kIODirectionIn);
        }
        return result;
    }
    
    if (arguments->structureOutput == nullptr || arguments->structureOutputSize != sizeof(s_perfCounterResults))
    {
        KextLog_Info("PerfTracing_ExportDataUserClient: structure output size %u, expected %lu\n", arguments->structureOutputSize, sizeof(s_perfCounterResults));
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
        
        // integer log2 = most significant set bit
        int intervalLog2 = 63 - __builtin_clzll(interval);
        atomic_fetch_add(&result->sampleBuckets[intervalLog2], 1);
    }
}

void InitProbe(PrjFSPerfCounter counter)
{
    s_perfCounterResults[counter] = PrjFSPerfCounterResult{ .min = UINT64_MAX };
}
