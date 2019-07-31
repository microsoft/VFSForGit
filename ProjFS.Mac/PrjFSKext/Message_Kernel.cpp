#include "Message_Kernel.hpp"
#include "ArrayUtilities.hpp"
#include <kern/debug.h>
#include <kern/assert.h>

void Message_Init(
    Message* spec,
    MessageHeader* header,
    uint64_t messageId,
    MessageType messageType,
    const FsidInode& fsidInode,
    int32_t pid,
    const char* procname,
    const char* path,
    const char* fromPath)
{
    header->messageId = messageId;
    header->messageType = messageType;
    header->fsidInode = fsidInode;
    header->pid = pid;
    
    if (nullptr != procname)
    {
        strlcpy(header->procname, procname, sizeof(header->procname));
    }
    else
    {
        header->procname[0] = '\0';
    }
    
    if (nullptr != path)
    {
        header->pathSizesBytes[MessagePath_Target] = strlen(path) + 1;
    }
    else
    {
        header->pathSizesBytes[MessagePath_Target] = 0;
    }

    if (nullptr != fromPath)
    {
        header->pathSizesBytes[MessagePath_From] = strlen(fromPath) + 1;
    }
    else
    {
        header->pathSizesBytes[MessagePath_From] = 0;
    }

    spec->messageHeader = header;
    spec->paths[MessagePath_Target] = path;
    spec->paths[MessagePath_From] = fromPath;
}

uint32_t Message_Encode(void* buffer, const uint32_t bufferSize, const Message& message)
{
    uint8_t* bufferPosition = static_cast<uint8_t*>(buffer);
    uint32_t bufferBytesRemain = bufferSize;
    
    assert(bufferSize >= sizeof(*message.messageHeader));
    memcpy(bufferPosition, message.messageHeader, sizeof(*message.messageHeader));
    bufferPosition +=    sizeof(*message.messageHeader);
    bufferBytesRemain -= sizeof(*message.messageHeader);
    
    for (unsigned i = 0; i < Array_Size(message.messageHeader->pathSizesBytes); ++i)
    {
        uint16_t stringSize = message.messageHeader->pathSizesBytes[i];
        if (stringSize > 0)
        {
            assert(bufferSize >= stringSize);
            memcpy(bufferPosition, message.paths[i], stringSize);
            bufferPosition += stringSize;
            bufferBytesRemain -= stringSize;
        }
    }
    
    return bufferSize - bufferBytesRemain;
}
