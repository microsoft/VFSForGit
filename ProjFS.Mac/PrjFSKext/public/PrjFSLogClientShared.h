#pragma once

#include <stdint.h>

// External method selectors for log user clients
enum PrjFSLogUserClientSelector
{
    LogSelector_Invalid = 0,
    
    LogSelector_FetchProfilingData,
};

enum PrjFSLogUserClientMemoryType
{
    LogMemoryType_Invalid = 0,
    
    LogMemoryType_MessageQueue,
};

enum PrjFSLogUserClientPortType
{
    LogPortType_Invalid = 0,
    
    LogPortType_MessageQueue,
};

enum KextLog_Level : uint32_t
{
    KEXTLOG_ERROR = 0,
    KEXTLOG_INFO = 1,
    KEXTLOG_NOTE,
};

struct KextLog_MessageHeader
{
    uint32_t flags;
    KextLog_Level level;
    uint64_t machAbsoluteTimestamp;
    char logString[0];
};

enum KextLog_MessageFlag
{
    LogMessageFlag_LogMessagesDropped = 0x1,
    LogMessageFlag_LogMessageTruncated = 0x2,
};

