#ifndef Message_h
#define Message_h

#include <sys/param.h>
#include "PrjFSCommon.h"

typedef enum
{
    MessageType_Invalid = 0,
    
    // Messages from user mode to kernel
    MessageType_UtoK_StartVirtualizationInstance,
    MessageType_UtoK_StopVirtualizationInstance,
    
    // Messages from kernel to user mode
    MessageType_KtoU_EnumerateDirectory,
    MessageType_KtoU_HydrateFile,
    
    // Responses
    MessageType_Response_Success,
    MessageType_Response_Fail,
    
} MessageType;

struct MessageHeader
{
    // The message id is used to correlate a response to its request
    uint64_t            messageId;
    
    // The message type indicates the type of request or response
    uint32_t            messageType; // values of type MessageType
    
    // For messages from kernel to user mode, indicates the PID of the process that initiated the I/O
    int32_t             pid;
    char                procname[MAXCOMLEN + 1];

    // Size of the flexible-length, nul-terminated path following the message body, including the nul character.
    uint16_t            pathSizeBytes;
};

// Description of a decomposed, in-memory message header plus variable length string field
struct Message
{
    const MessageHeader* messageHeader;
    const char*    path;
};

void Message_Init(
    Message* spec,
    MessageHeader* header,
    uint64_t messageId,
    MessageType messageType,
    int32_t pid,
    const char* procname,
    const char* path);

#endif /* Message_h */
