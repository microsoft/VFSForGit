#include "PrjFSUser.hpp"
#include "kext-perf-tracing.hpp"
#include "../../PrjFSKext/public/PrjFSLogClientShared.h"
#include <iostream>
#include <dispatch/queue.h>
#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOKitLib.h>
#include <mach/mach_time.h>

static const char* KextLogLevelAsString(KextLog_Level level);
static uint64_t NanosecondsFromAbsoluteTime(uint64_t machAbsoluteTime);
static dispatch_source_t StartKextProfilingDataPolling(io_connect_t connection);
static void ProcessLogMessagesOnConnection(io_connect_t connection, io_service_t prjfsService);

static mach_timebase_info_data_t s_machTimebase;
static uint64_t s_machStartTime;
static IONotificationPortRef s_notificationPort;

int main(int argc, const char * argv[])
{
    mach_timebase_info(&s_machTimebase);
    s_machStartTime = mach_absolute_time();


    s_notificationPort = IONotificationPortCreate(kIOMasterPortDefault);
    IONotificationPortSetDispatchQueue(s_notificationPort, dispatch_get_main_queue());

    PrjFSService_WatchContext* watchContext = PrjFSService_WatchForServiceAndConnect(
        s_notificationPort, UserClientType_Log,
        [](io_service_t service, io_connect_t connection, bool serviceVersionMismatch, IOReturn connectResult, PrjFSService_WatchContext* context)
        {
            if (connection != IO_OBJECT_NULL)
            {
                ProcessLogMessagesOnConnection(connection, service);
            }
            else
            {
                std::cerr << "Failed to connect to matched kernel service; result = 0x" << std::hex << connectResult << std::endl;
            }
        });
    if (nullptr == watchContext)
    {
        std::cerr << "Failed to register for IOService notifications.\n";
        return 1;
    }
    

    CFRunLoopRun();
    
    PrjFSService_StopWatching(watchContext);

    return 0;
}

struct LogConnectionState
{
    DataQueueResources dataQueue;
    unsigned lineCount;
};

static void ProcessLogMessagesOnConnection(io_connect_t connection, io_service_t prjfsService)
{
    std::shared_ptr<LogConnectionState> logState(new LogConnectionState {{}, 0 });
    if (!PrjFSService_DataQueueInit(&logState->dataQueue, connection, LogPortType_MessageQueue, LogMemoryType_MessageQueue, dispatch_get_main_queue()))
    {
        std::cerr << "Failed to set up shared data queue on connection 0x" << std::hex << connection << ".\n";
        IOServiceClose(connection);
        return;
    }
    
    uint64_t prjfsServiceEntryID = 0;
    IORegistryEntryGetRegistryEntryID(prjfsService, &prjfsServiceEntryID);
    uint64_t timeOffsetMS = NanosecondsFromAbsoluteTime(mach_absolute_time() - s_machStartTime) / NSEC_PER_MSEC;
    printf("(0x%x: %5d: %5llu.%03llu) START: Processing log messages from service with ID 0x%llx\n",
        connection, logState->lineCount, timeOffsetMS / 1000u, timeOffsetMS % 1000u, prjfsServiceEntryID);
    fflush(stdout);
    ++logState->lineCount;
    
    dispatch_source_set_event_handler(logState->dataQueue.dispatchSource, ^{
        DataQueue_ClearMachNotification(logState->dataQueue.notificationPort);
        
        while(IODataQueueEntry* entry = DataQueue_Peek(logState->dataQueue.queueMemory))
        {
            int messageSize = entry->size;
            if (messageSize >= sizeof(KextLog_MessageHeader) + 2)
            {
                struct KextLog_MessageHeader message = {};
                memcpy(&message, entry->data, sizeof(KextLog_MessageHeader));
                const char* messageType = KextLogLevelAsString(message.level);
                int logStringLength = messageSize - sizeof(KextLog_MessageHeader) - 1;
                
                uint64_t timeOffsetNS = NanosecondsFromAbsoluteTime(message.machAbsoluteTimestamp - s_machStartTime);
                uint64_t timeOffsetMS = timeOffsetNS / NSEC_PER_MSEC;
                
                printf("(0x%x: %5d: %5llu.%03llu) %s: %.*s\n", connection, logState->lineCount, timeOffsetMS / 1000u, timeOffsetMS % 1000u, messageType, logStringLength, entry->data + sizeof(KextLog_MessageHeader));
                logState->lineCount++;
            }
            
            DataQueue_Dequeue(logState->dataQueue.queueMemory, nullptr, nullptr);
        }
        
        fflush(stdout);
    });
    dispatch_resume(logState->dataQueue.dispatchSource);

    dispatch_source_t timer = nullptr;
    if (PrjFSLog_FetchAndPrintKextProfilingData(connection))
    {
        timer = StartKextProfilingDataPolling(connection);
    }

    PrjFSService_WatchForServiceTermination(
        prjfsService,
        s_notificationPort,
        [timer, connection, logState, prjfsServiceEntryID]()
        {
            uint64_t timeOffsetMS = NanosecondsFromAbsoluteTime(mach_absolute_time() - s_machStartTime) / NSEC_PER_MSEC;
            printf("(0x%x: %5d: %5llu.%03llu) STOP: service with ID 0x%llx has terminated\n", connection, logState->lineCount, timeOffsetMS / 1000u, timeOffsetMS % 1000u, prjfsServiceEntryID);
            fflush(stdout);
            logState->lineCount++;

            DataQueue_Dispose(&logState->dataQueue, connection, LogMemoryType_MessageQueue);
            
            if (nullptr != timer)
            {
                dispatch_cancel(timer);
                dispatch_release(timer);
            }
            
            IOServiceClose(connection);
        });
}

static const char* KextLogLevelAsString(KextLog_Level level)
{
    switch (level)
    {
    case KEXTLOG_ERROR:
        return "Error";
    case KEXTLOG_INFO:
        return "Info";
    case KEXTLOG_DEFAULT:
        return "Default";
    default:
        return "Unknown";
    }
}

static uint64_t NanosecondsFromAbsoluteTime(uint64_t machAbsoluteTime)
{
    return static_cast<__uint128_t>(machAbsoluteTime) * s_machTimebase.numer / s_machTimebase.denom;
}

static dispatch_source_t StartKextProfilingDataPolling(io_connect_t connection)
{
    dispatch_source_t timer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, dispatch_get_main_queue());
    dispatch_source_set_timer(timer, DISPATCH_TIME_NOW, 15 * NSEC_PER_SEC, 10 * NSEC_PER_SEC);
    dispatch_source_set_event_handler(timer, ^{
        PrjFSLog_FetchAndPrintKextProfilingData(connection);
    });
    dispatch_resume(timer);
    return timer;
}

