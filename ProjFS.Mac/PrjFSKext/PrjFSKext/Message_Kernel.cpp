#include <kern/debug.h>
#include "Message.h"

void Message_Init(
    Message* spec,
    MessageHeader* header,
    uint64_t messageId,
    MessageType messageType,
    int32_t pid,
    const char* procname,
    const char* path)
{
    header->messageId = messageId;
    header->messageType = messageType;
    
    if (nullptr != path)
    {
        header->pathSizeBytes = strlen(path) + 1;
    }
    else
    {
        header->pathSizeBytes = 0;
    }
    
    spec->messageHeader = header;
    spec->path = path;
}
