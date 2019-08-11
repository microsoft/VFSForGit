#include <IOKit/IOKitLib.h>
#include <cstdio>
#include "../PrjFSKext/public/PrjFSCommon.h"
#include "../PrjFSKext/public/PrjFSEventTraceClientShared.h"
#include "../PrjFSLib/PrjFSUser.hpp"
#include <unistd.h>
#include <getopt.h>
#include <cstdlib>
#include <type_traits>

using std::strtoul;
using std::fprintf;
using std::extent;

struct EventTraceSettings
{
    const char* pathFilterString;
    uint64_t filterFlags;
    uint64_t vnodeActionMask;
};

static bool GenerateEventTracingSettings(int argc, char* argv[], EventTraceSettings& outSettings)
{
    const char* pathFilterString = NULL;
    int tracingEnabled = 0, tracingDisabled = 0;
    uint32_t vnodeActionFilter = UINT32_MAX;
    int traceAllVnodeEvents = 0, traceDeniedVnodeEvents = 0, traceProviderMessagingEvents = 0, traceCrawlerEvents = 0;
    int traceAllFileopEvents = 0;

    int ch;

    struct option longopts[] = {
         { "enable",               no_argument,            &tracingEnabled,  1 },
         { "disable",              no_argument,            &tracingDisabled, 1 },
         { "vnode-events-denied",  no_argument,            &traceDeniedVnodeEvents, 1 },
         { "vnode-message-events", no_argument,            &traceProviderMessagingEvents, 1 },
         { "vnode-crawler-events", no_argument,            &traceCrawlerEvents, 1 },
         { "vnode-all-events",     no_argument,            &traceAllVnodeEvents, 1 },
         { "fileop-all-events",    no_argument,            &traceAllFileopEvents, 1 },
         { "path-filter",          required_argument,      NULL,             'p' },
         { "vnode-action-filter",  required_argument,      NULL,             'a' },
         { NULL,                   0,                      NULL,             0 }
    };

    while ((ch = getopt_long(argc, argv, "bf:", longopts, NULL)) != -1)
    {
        switch (ch)
        {
        case 'a':
            {
                char* end = NULL;
                unsigned long filter = strtoul(optarg, &end, 16 /* base */);
                if (end != optarg && end != NULL && filter <= UINT32_MAX)
                {
                    vnodeActionFilter = (uint32_t)filter;
                }
                else
                {
                    fprintf(stderr, "--vnode-action-filter: Bad filter mask value, must be in range 0-ffffffff");
                    return false;
                }
            }
            
            break;
        case 'p':
            if (pathFilterString != NULL)
            {
                fprintf(stderr, "--path-filter: Currently only one path filter is supported\n");
                return false;
            }
            
            pathFilterString = optarg;
            
            break;
        case 0:
            printf("Processing argument %s\n", argv[optind]);
            break;
        default:
            fprintf(stderr, "TODO: %u\n", ch);
            return false;
        }
    }
    
    if ((tracingEnabled ^ tracingDisabled) == 0)
    {
        fprintf(stderr, "Must use exactly one of --enable or --disable");
        return false;
    }
    else if (pathFilterString == NULL)
    {
        fprintf(stderr, "--path-filter is required");
        return false;
    }
    else if (tracingEnabled && !(traceAllVnodeEvents || traceDeniedVnodeEvents || traceProviderMessagingEvents))
    {
        fprintf(stderr, "Warning: tracing enabled, but no base vnode event filter selected, this means no events will be traced.\n");
    }

    if (tracingDisabled)
    {
        if (pathFilterString != NULL)
        {
            CFRelease(pathFilterString);
        }
        outSettings = EventTraceSettings{ "", 0, 0 };
        return true;
    }
    else
    {
        outSettings = EventTraceSettings
            {
                pathFilterString,
                ((traceCrawlerEvents           ? EventTraceFilter_Vnode_Crawler         : 0) |
                 (traceDeniedVnodeEvents       ? EventTraceFilter_Vnode_Denied          : 0) |
                 (traceProviderMessagingEvents ? EventTraceFilter_Vnode_ProviderMessage : 0) |
                 (traceAllVnodeEvents          ? EventTraceFilter_Vnode_All             : 0)),
                vnodeActionFilter
            };
        return true;
    }
}

int main(int argc, char* argv[])
{
    EventTraceSettings traceSettings = {};
    if (!GenerateEventTracingSettings(argc, argv, traceSettings))
    {
        fprintf(stderr, "Failed to generate tracing settings\n");
        return 1;
    }

    io_connect_t eventTraceConnection = PrjFSService_ConnectToDriver(UserClientType_EventTrace);
    if (eventTraceConnection == IO_OBJECT_NULL)
    {
        fprintf(stderr, "Connecting to PrjFS Service failed.\n");
        return 1;
    }
    
    uint64_t scalarInputs[] = { traceSettings.filterFlags, traceSettings.vnodeActionMask };
    IOReturn result = IOConnectCallMethod(
        eventTraceConnection, EventTraceSelector_SetEventTracingMode,
        scalarInputs, extent<decltype(scalarInputs)>::value,
        traceSettings.pathFilterString, strlen(traceSettings.pathFilterString) + 1,
        nullptr, nullptr,
        nullptr, nullptr);
    if (result != kIOReturnSuccess)
    {
        fprintf(stderr, "SetEventTracingMode call failed: 0x%x.\n", result);
        return 1;
    }
    
    CFRunLoopRun();
    
    IOServiceClose(eventTraceConnection);
    
    return 0;
}
