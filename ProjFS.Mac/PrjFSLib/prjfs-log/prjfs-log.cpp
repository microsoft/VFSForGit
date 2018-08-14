#include "../PrjFSUser.hpp"
#include "../../PrjFSKext/public/PrjFSLogClientShared.h"
#include <iostream>
#include <dispatch/queue.h>
#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOKitLib.h>
#include <mach/mach_time.h>

static const char* KextLogLevelAsString(KextLog_Level level);

int main(int argc, const char * argv[])
{
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
                
                printf("(%d: %llu) %s: %.*s\n", lineCount, message.machAbsoluteTimestamp, messageType, logStringLength, entry->data + sizeof(KextLog_MessageHeader));
                lineCount++;
            }
            
            IODataQueueDequeue(dataQueue.queueMemory, nullptr, nullptr);
        }
    });
    dispatch_resume(dataQueue.dispatchSource);
    CFRunLoopRun();
    
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
