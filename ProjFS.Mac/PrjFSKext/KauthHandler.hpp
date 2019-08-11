#ifndef KauthHandler_h
#define KauthHandler_h

#include "public/Message.h"
#include "VirtualizationRoots.hpp"

kern_return_t KauthHandler_Init();
kern_return_t KauthHandler_Cleanup();

struct KauthHandlerEventTracingSettings
{
    const char*    pathPrefixFilter;
    kauth_action_t vnodeActionFilterMask;
    bool           traceDeniedVnodeEvents;
    bool           traceProviderMessagingVnodeEvents;
    bool           traceAllVnodeEvents;
    bool           traceCrawlerEvents;
};

bool KauthHandler_EnableTraceListeners(bool tracingEnabled, const KauthHandlerEventTracingSettings& settings);
bool KauthHandler_IsFileSystemCrawler(const char* procname);

#endif /* KauthHandler_h */
