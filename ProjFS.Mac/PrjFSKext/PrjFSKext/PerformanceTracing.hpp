#pragma once

#include "PrjFSCommon.h"
#include "PrjFSPerfCounter.h"

#include <mach/mach_time.h>
#include <IOKit/IOReturn.h>

void PerfTracing_Init();
void PerfTracing_RecordSample(PrjFSPerfCounter counter, uint64_t startTime, uint64_t endTime);

struct IOExternalMethodArguments;
IOReturn PerfTracing_ExportDataUserClient(IOExternalMethodArguments* arguments);

class PerfTracer
{
public:
    inline void IncrementCount(PrjFSPerfCounter counter);
    inline bool IsEnabled();
};

void PerfTracer::IncrementCount(PrjFSPerfCounter counter)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    PerfTracing_RecordSample(counter, 0, 0);
#endif
}

bool PerfTracer::IsEnabled()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    // TODO: in the constructor, decide if this tracer instance is enabled or not based on
    // a desired sampling rate. We create one tracer instance per vnode/fileop callback,
    // and that tracer instance is used to initialize all PerfSamples for the duration of that
    // callback. So by performing the sampling calculation once per tracer, we will gather
    // all or nothing of a sample set of callbacks, and therefore gather a representative
    // view of the relative amounts of time spent on each sample
    return true;
#else
    return false;
#endif
}

class PerfSample
{
private:
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    PerfTracer* perfTracer;
    PrjFSPerfCounter counter;
    uint64_t startTimestamp;
#endif

public:
    inline PerfSample(PerfTracer* perfTracer, PrjFSPerfCounter counter);
    inline ~PerfSample();
};

PerfSample::PerfSample(PerfTracer* perfTracer, PrjFSPerfCounter counter)
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    :
    perfTracer(perfTracer),
    counter(counter),
    startTimestamp(perfTracer->IsEnabled() ? mach_absolute_time() : 0)
#endif
{
}

PerfSample::~PerfSample()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    if (this->perfTracer->IsEnabled())
    {
        PerfTracing_RecordSample(this->counter, this->startTimestamp, mach_absolute_time());
    }
#endif
}
