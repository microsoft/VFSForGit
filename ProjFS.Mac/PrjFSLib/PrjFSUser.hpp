#pragma once

#include "../PrjFSKext/public/PrjFSCommon.h"
#include "../PrjFSKext/public/PrjFSProviderClientShared.h"
#include <IOKit/IODataQueueClient.h>
#include <dispatch/dispatch.h>
#include <IOKit/IOTypes.h>

struct DataQueueResources
{
    mach_port_t notificationPort;
    IODataQueueMemory* queueMemory;
    mach_vm_address_t queueMemoryAddress;
    mach_vm_address_t queueMemorySize;
    dispatch_source_t dispatchSource;
};

io_connect_t PrjFSService_ConnectToDriver(enum PrjFSServiceUserClientType clientType);

bool PrjFSService_DataQueueInit(
    DataQueueResources* outQueue,
    io_connect_t connection,
    uint32_t clientPortType,
    uint32_t clientMemoryType,
    dispatch_queue_t eventHandlingQueue);
