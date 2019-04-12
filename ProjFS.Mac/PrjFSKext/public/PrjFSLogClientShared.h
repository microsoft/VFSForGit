#pragma once

#include <stdint.h>

// External method selectors for log user clients
enum PrjFSLogUserClientSelector
{
    LogSelector_Invalid = 0,
    
    LogSelector_FetchProfilingData,
    LogSelector_FetchHealthData,
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

// Log message levels, these correspond to their os_log counterparts
enum KextLog_Level : uint32_t
{
    KEXTLOG_ERROR = 0,   // For important failures, always gets logged and causes INFO messages to be committed too.
    KEXTLOG_DEFAULT = 2, // Run-of-the-mill messages that shouldn't be too frequent as they always are logged to disk.
    KEXTLOG_INFO = 1,    // Verbose logging that might help with diagnosing problems; these don't usually get logged to disk, except when an ERROR is loggod.
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

