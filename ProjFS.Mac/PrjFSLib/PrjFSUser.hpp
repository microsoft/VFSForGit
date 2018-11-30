#pragma once

#include "../PrjFSKext/public/PrjFSCommon.h"
#include "../PrjFSKext/public/PrjFSProviderClientShared.h"
#include <IOKit/IODataQueueClient.h>
#include <dispatch/dispatch.h>
#include <IOKit/IOTypes.h>
#include <functional>

struct DataQueueResources
{
    mach_port_t notificationPort;
    IODataQueueMemory* queueMemory;
    mach_vm_address_t queueMemoryAddress;
    mach_vm_address_t queueMemorySize;
    dispatch_source_t dispatchSource;
};

io_connect_t PrjFSService_ConnectToDriver(enum PrjFSServiceUserClientType clientType);

struct PrjFSService_WatchContext;
PrjFSService_WatchContext* PrjFSService_WatchForServiceAndConnect(
    struct IONotificationPort* notificationPort,
    enum PrjFSServiceUserClientType clientType,
    std::function<void(io_service_t, io_connect_t, PrjFSService_WatchContext*)> discoveryCallback);
void PrjFSService_StopWatching(PrjFSService_WatchContext* context);
bool PrjFSServiceValidateVersion(io_service_t prjfsService);

bool PrjFSService_DataQueueInit(
    DataQueueResources* outQueue,
    io_connect_t connection,
    uint32_t clientPortType,
    uint32_t clientMemoryType,
    dispatch_queue_t eventHandlingQueue);

IODataQueueEntry* DataQueue_Peek(IODataQueueMemory* dataQueue);
IOReturn DataQueue_Dequeue(IODataQueueMemory* dataQueue, void* data, uint32_t* dataSize);
