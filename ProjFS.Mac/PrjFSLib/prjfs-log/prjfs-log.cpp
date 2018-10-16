#include "../PrjFSUser.hpp"
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

static mach_timebase_info_data_t s_machTimebase;


int main(int argc, const char * argv[])
{
    mach_timebase_info(&s_machTimebase);
    const uint64_t machStartTime = mach_absolute_time();

    io_connect_t connection = PrjFSService_ConnectToDriver(UserClientType_Log);
    if (connection == IO_OBJECT_NULL)
    {
        std::cerr << "Failed to connect to kernel service.\n";
        return 1;
    }
    
    DataQueueResources dataQueue = {};
    if (!PrjFSService_DataQueueInit(&dataQueue, connection, LogPortType_MessageQueue, LogMemoryType_MessageQueue, dispatch_get_main_queue()))
    {
        std::cerr << "Failed to set up shared data queue.\n";
        return 1;
    }
    
    __block int lineCount = 0;
    
    dispatch_source_set_event_handler(dataQueue.dispatchSource, ^{
        struct {
            mach_msg_header_t	msgHdr;
            mach_msg_trailer_t	trailer;
        } msg;
        mach_msg(&msg.msgHdr, MACH_RCV_MSG | MACH_RCV_TIMEOUT, 0, sizeof(msg), dataQueue.notificationPort, 0, MACH_PORT_NULL);
        
        while(true)
        {
            IODataQueueEntry* entry = IODataQueuePeek(dataQueue.queueMemory);
            if(entry == nullptr)
            {
                break;
            }
            
            int messageSize = entry->size;
            if (messageSize >= sizeof(KextLog_MessageHeader) + 2)
            {
                struct KextLog_MessageHeader message = {};
                memcpy(&message, entry->data, sizeof(KextLog_MessageHeader));
                const char* messageType = KextLogLevelAsString(message.level);
                int logStringLength = messageSize - sizeof(KextLog_MessageHeader) - 1;
                
                uint64_t timeOffsetNS = NanosecondsFromAbsoluteTime(message.machAbsoluteTimestamp - machStartTime);
                uint64_t timeOffsetMS = timeOffsetNS / NSEC_PER_MSEC;
                
                printf("(%d: %5llu.%03llu) %s: %.*s\n", lineCount, timeOffsetMS / 1000u, timeOffsetMS % 1000u, messageType, logStringLength, entry->data + sizeof(KextLog_MessageHeader));
                lineCount++;
            }
            
            IODataQueueDequeue(dataQueue.queueMemory, nullptr, nullptr);
        }
    });
    dispatch_resume(dataQueue.dispatchSource);

    dispatch_source_t timer = nullptr;
    if (PrjFSLog_FetchAndPrintKextProfilingData(connection))
    {
        timer = StartKextProfilingDataPolling(connection);
    }

    CFRunLoopRun();
    
    if (nullptr != timer)
    {
        dispatch_cancel(timer);
        dispatch_release(timer);
    }

    return 0;
}

static const char* KextLogLevelAsString(KextLog_Level level)
{
    switch (level)
    {
    case KEXTLOG_ERROR:
        return "Error";
    case KEXTLOG_INFO:
        return "Info";
    case KEXTLOG_NOTE:
        return "Note";
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

