#pragma once

#include "PrjFSCommon.h"

#include <mach/mach_time.h>
#include <IOKit/IOReturn.h>

void PerfTracing_Init();
void PerfTracing_ProbeInit(PerfTracingProbe* probe);
void PerfTracing_RecordSample(PerfTracingProbe* probe, uint64_t startTime, uint64_t endTime);

struct IOExternalMethodArguments;
IOReturn PerfTracing_ExportDataUserClient(IOExternalMethodArguments* arguments);

extern PerfTracingProbe profile_probes[Probe_Count];
// A scope-based manual instrumentation profiling mechanism.
// In the simplest case, the time between construction and destruction of the ProfileSample
// is measured and registered with the probe specified during construction:
//
// void MyFunction()
// {
//    ProfileSample functionSample(Probe_MyFunction);
//     // ... The code for which we're measuring the runtime ...
// } // <-- functionSample goes out of scope here, so that's when timing ends
//
//
// To allow runtimes different code paths in the same scope to be recorded separately, the
// probe identity can be modified using SetProbe():
//
// void MyFunction()
// {
//     ProfileSample functionSample(Probe_MyFunction);
//     // ... The code for which we're measuring the runtime ...
//     if (specialCase)
//     {
//         // We want to be able to distinguish between the runtimes of MyFunction for this
//         // special case vs "normal" runs.
//         functionSample.SetProbe(Probe_MyFunctionSpecialCase);
//         // ... do something potentially expensive here ...
//     }
//     // ... more code ...
// } // <-- Runtime to here will be recorded either under Probe_MyFunction or Probe_MyFunctionSpecialCase
//
//
// For tracing sub-sections of code, such as the special case logic above, in isolation,
// we have 2 options: taking additional scoped samples, or split timings. Scoped samples are
// easier to understand:
//
// void MyFunction()
// {
//     ProfileSample functionSample(Probe_MyFunction);
//     // ...
//     if (specialCase)
//     {
//         // Measure only the special case code on its own:
//         ProfileSample specialCaseSample(Probe_MyFunctionSpecialCase);
//         // ... do something potentially expensive here ...
//     } // <-- scope of specialCaseSample ends here
//     // ... more code ...
// } // <-- end of Probe_MyFunction in all cases
//
// In the above example, the runtimes of all MyFunction() calls are recorded under Probe_MyFunction,
// while the special case code on its own is recorded in Probe_MyFunctionSpecialCase.
//
// Taking split timings meanwhile allows us to carve up scoped samples into constituent sub-samples,
// useful for drilling down to find the source of performance issues:
//
// void MyFunction()
// {
//     ProfileSample functionSample(Probe_MyFunction);
//     // ...
//     // The time from the creation of functionSample to this point is recorded as Probe_MyFunctionPart1.
//     functionSample.TakeSplitSample(Probe_MyFunctionPart1, Probe_MyFunctionRemainder);
//     if (specialCase)
//     {
//         // ... do something potentially expensive here ...
//         functionSample.TakeSplitSample(Probe_MyFunctionSpecialCase); // This measures time since the Part1 split
//     } // <-- scope of specialCaseSample ends here
//     // ... more code ...
// } // <-- end of Probe_MyFunction in all cases; the time since the last split (Probe_MyFunctionPart1
//   // or Probe_MyFunctionSpecialCase depending on code path) is recorded as Probe_MyFunctionRemainder.
//
// The end time stamp for a split is taken as the start of the next split, and the overall start and end
// stamps of the scoped sample are alse the start and end of the first and last (remainder) split,
// respectively.
// So in this case, the sum total runtime of all samples of Probe_MyFunction is exactly equal to the sum of
// Probe_MyFunctionPart1 + Probe_MyFunctionSpecialCase + Probe_MyFunctionRemainder.
// Note that Probe_MyFunctionSpecialCase may have a lower sample count.
// Note also that the "remainder" split is optional - if only the 1-argument version of TakeSplitSample
// is used, the split time to the end of scope is not recorded. (And like the scoped sample, it can be changed,
// in this case using SetFinalSplitProbe())
class ProfileSample
{
    ProfileSample(const ProfileSample&) = delete;
    ProfileSample() = delete;

#if PRJFS_PERFORMANCE_TRACING_ENABLE
    const uint64_t startTimestamp;
    PrjFS_PerfCounter wholeSampleProbe;
    PrjFS_PerfCounter finalSplitProbe;
    uint64_t splitTimestamp;
#endif

public:
    inline ProfileSample(PrjFS_PerfCounter defaultProbe);
    inline void SetProbe(PrjFS_PerfCounter probe);
    inline void TakeSplitSample(PrjFS_PerfCounter splitProbe);
    inline void TakeSplitSample(PrjFS_PerfCounter splitProbe, PrjFS_PerfCounter newFinalSplitProbe);
    inline void SetFinalSplitProbe(PrjFS_PerfCounter newFinalSplitProbe);
    inline ~ProfileSample();
};

ProfileSample::~ProfileSample()
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    uint64_t endTimestamp = mach_absolute_time();
    if (this->wholeSampleProbe != Probe_None)
    {
        PerfTracing_RecordSample(&profile_probes[this->wholeSampleProbe], this->startTimestamp, endTimestamp);
    }
    
    if (this->finalSplitProbe != Probe_None)
    {
        PerfTracing_RecordSample(&profile_probes[this->finalSplitProbe], this->splitTimestamp, endTimestamp);
    }
#endif
};

void ProfileSample::TakeSplitSample(PrjFS_PerfCounter splitProbe)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    uint64_t newSplitTimestamp = mach_absolute_time();
    PerfTracing_RecordSample(&profile_probes[splitProbe], this->splitTimestamp, newSplitTimestamp);
    this->splitTimestamp = newSplitTimestamp;
#endif
}

void ProfileSample::TakeSplitSample(PrjFS_PerfCounter splitProbe, PrjFS_PerfCounter newFinalSplitProbe)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    this->TakeSplitSample(splitProbe);
    this->finalSplitProbe = newFinalSplitProbe;
#endif
}

void ProfileSample::SetFinalSplitProbe(PrjFS_PerfCounter newFinalSplitProbe)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    this->finalSplitProbe = newFinalSplitProbe;
#endif
}


ProfileSample::ProfileSample(PrjFS_PerfCounter defaultProbe)
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    :
    startTimestamp(mach_absolute_time()),
    wholeSampleProbe(defaultProbe),
    finalSplitProbe(Probe_None),
    splitTimestamp(this->startTimestamp)
#endif
{
}

void ProfileSample::SetProbe(PrjFS_PerfCounter probe)
{
#if PRJFS_PERFORMANCE_TRACING_ENABLE
    this->wholeSampleProbe = probe;
#endif
}

