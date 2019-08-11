#pragma once

#include <stdint.h>

// External method selectors for event trace user clients
enum PrjFSEventTraceUserClientSelector
{
    EventTraceSelector_Invalid = 0,
	
    EventTraceSelector_SetEventTracingMode,
};

enum EventTraceFilterFlags : uint64_t
{
    EventTraceFilter_Vnode_Denied = 0x1,
    EventTraceFilter_Vnode_ProviderMessage = 0x2,
    EventTraceFilter_Vnode_All = 0x4,
    EventTraceFilter_Vnode_Crawler = 0x8,

    // TODO(Mac): Add fileop trace modes
};
