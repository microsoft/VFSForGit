#include "PerformanceTracing.hpp"
#include "KextLog.hpp"
#include <sys/types.h>
#include <stdatomic.h>
#include <IOKit/IOUserClient.h>

#if PRJFS_PERFORMANCE_TRACING_ENABLE
uint64_t PerfTracer::s_numTracers = 0;
#endif

static PrjFSPerfCounterResult s_perfCounterResults[PrjFSPerfCounter_Count];

static void InitProbe(PrjFSPerfCounter counter);

#if PRJFS_PERFORMANCE_TRACING_ENABLE
static int Log2(unsigned long long nonZeroValue);
#endif

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
#if PRJFS_PERFORMANCE_TRACING_ENABLE
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
        
        int intervalLog2 = Log2(interval);
        atomic_fetch_add(&result->sampleBuckets[intervalLog2], 1);
    }
#endif
}

static void InitProbe(PrjFSPerfCounter counter)
{
    s_perfCounterResults[counter] = PrjFSPerfCounterResult{ .min = UINT64_MAX };
}

#if PRJFS_PERFORMANCE_TRACING_ENABLE
// Computes the floor of the base-2 logarithm of the provided positive integer.
// The __builtin_clzll() function counts the number of 0 bits until the most
// significant 1 bit in the argument. For log2, the position of this most
// significant 1 bit counting from the least significant bit is needed.
static int Log2(unsigned long long nonZeroValue)
{
    static const int maxBitIndex = sizeof(nonZeroValue) * CHAR_BIT - 1;
    return maxBitIndex - __builtin_clzll(nonZeroValue);
}
#endif
