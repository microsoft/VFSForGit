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
bool PrjFSService_ValidateVersion(io_service_t prjfsService);

void PrjFSService_WatchForServiceTermination(io_service_t service, struct IONotificationPort* notificationPort, std::function<void()> terminationCallback);


bool PrjFSService_DataQueueInit(
    DataQueueResources* outQueue,
    io_connect_t connection,
    uint32_t clientPortType,
    uint32_t clientMemoryType,
    dispatch_queue_t eventHandlingQueue);
void DataQueue_Dispose(DataQueueResources* queueResources, io_connect_t connection, uint32_t clientMemoryType);

IODataQueueEntry* DataQueue_Peek(IODataQueueMemory* dataQueue);
IOReturn DataQueue_Dequeue(IODataQueueMemory* dataQueue, void* data, uint32_t* dataSize);
void DataQueue_ClearMachNotification(mach_port_t port);
