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
private:
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    static uint64_t s_numTracers;
    bool isEnabled;
#endif

public:
    inline PerfTracer();
    inline bool IsEnabled();
    inline void IncrementCount(PrjFSPerfCounter counter);
};

inline PerfTracer::PerfTracer()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    // Set this value to N for a sampling rate of 1/N
    const int sampleEveryNthTracer = 100;

    // This increment is not thread-safe, and that is ok. If there is a race here, we will
    // sometimes under-count and sometimes over-count, but it should all balance out.
    // And if we want to capture every event, we can simply replace this logic with
    // this->isEnabled = true;
    uint64_t id = s_numTracers++;

    this->isEnabled = (id % sampleEveryNthTracer == 0);
#endif
}

inline bool PerfTracer::IsEnabled()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    return this->isEnabled;
#else
    return false;
#endif
}

inline void PerfTracer::IncrementCount(PrjFSPerfCounter counter)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    if (this->IsEnabled())
    {
        PerfTracing_RecordSample(counter, 0, 0);
    }
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

inline PerfSample::PerfSample(PerfTracer* perfTracer, PrjFSPerfCounter counter)
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    :
    perfTracer(perfTracer),
    counter(counter),
    startTimestamp(perfTracer->IsEnabled() ? mach_absolute_time() : 0)
#endif
{
}

inline PerfSample::~PerfSample()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    if (this->perfTracer->IsEnabled())
    {
        PerfTracing_RecordSample(this->counter, this->startTimestamp, mach_absolute_time());
    }
#endif
}
