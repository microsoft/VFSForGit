#ifndef Message_h
#define Message_h

#include <sys/param.h>
#include "PrjFSCommon.h"
#include "FsidInode.h"

typedef enum
{
    MessageType_Invalid = 0,
	
    // Messages from kernel to user mode
    MessageType_KtoU_EnumerateDirectory,
    MessageType_KtoU_RecursivelyEnumerateDirectory,
    MessageType_KtoU_HydrateFile,
    
    MessageType_KtoU_NotifyFileModified,
    MessageType_KtoU_NotifyFilePreDelete,
    MessageType_KtoU_NotifyFilePreDeleteFromRename,
    MessageType_KtoU_NotifyDirectoryPreDelete,
    MessageType_KtoU_NotifyFileCreated,
    MessageType_KtoU_NotifyFileRenamed,
    MessageType_KtoU_NotifyDirectoryRenamed,
    MessageType_KtoU_NotifyFileHardLinkCreated,
    MessageType_KtoU_NotifyFilePreConvertToFull,
    
    // Responses
    MessageType_Response_Success,
    MessageType_Response_Fail,
    
    // Other message outcomes
    MessageType_Result_Aborted,
    
} MessageType;

enum MessagePathField
{
    MessagePath_Target = 0,
    MessagePath_From,
    
    MessagePath_Count,
};

struct MessageHeader
{
    // The message id is used to correlate a response to its request
    uint64_t            messageId;
    
    // The message type indicates the type of request or response
    uint32_t            messageType; // values of type MessageType
    
    // fsid and inode of the file
    FsidInode           fsidInode;
    
    // For messages from kernel to user mode, indicates the PID of the process that initiated the I/O
    int32_t             pid;
    char                procname[MAXCOMLEN + 1];

    // Sizes of the flexible-length, nul-terminated paths following the message body.
    // Sizes include the nul characters but can be 0 to indicate total absence.
    uint16_t            pathSizesBytes[MessagePath_Count];
};

// Description of a decomposed, in-memory message header plus variable length string field
struct Message
{
    const MessageHeader* messageHeader;
    const char* paths[MessagePath_Count];
};


uint32_t Message_EncodedSize(const MessageHeader* messageHeader);
const char* Message_MessageTypeString(MessageType messageType);

#endif /* Message_h */
