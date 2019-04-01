#include "MockPerfTracing.hpp"
#include <string>

#if PRJFS_PERFORMANCE_TRACING_ENABLE
uint64_t PerfTracer::s_numTracers = 0;
#endif

void PerfTracing_RecordSample(PrjFSPerfCounter counter, uint64_t startTime, uint64_t endTime)
{
}
