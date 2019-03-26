#pragma once

#include <stdint.h>
#include "../PrjFSKext/public/PrjFSPerfCounter.h"

void PerfTracing_RecordSample(PrjFSPerfCounter counter, uint64_t startTime, uint64_t endTime);
