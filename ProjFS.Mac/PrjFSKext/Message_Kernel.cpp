#include <kern/debug.h>
#include <kern/assert.h>
#include "public/Message.h"

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
        header->pathSizeBytes = strlen(path) + 1;
    }
    else
    {
        header->pathSizeBytes = 0;
    }

    if (nullptr != fromPath)
    {
        header->fromPathSizeBytes = strlen(fromPath) + 1;
    }
    else
    {
        header->fromPathSizeBytes = 0;
    }

    spec->messageHeader = header;
    spec->path = path;
    spec->fromPath = fromPath;
}

uint32_t Message_EncodedSize(const Message& message)
{
    return sizeof(*message.messageHeader) + message.messageHeader->pathSizeBytes + message.messageHeader->fromPathSizeBytes;
}

uint32_t Message_Encode(void* buffer, const uint32_t bufferSize, const Message& message)
{
    uint8_t* bufferPosition = static_cast<uint8_t*>(buffer);
    uint32_t bufferBytesRemain = bufferSize;
    
    assert(bufferBytesRemain >= sizeof(*message.messageHeader));
    memcpy(bufferPosition, message.messageHeader, sizeof(*message.messageHeader));
    bufferPosition +=    sizeof(*message.messageHeader);
    bufferBytesRemain -= sizeof(*message.messageHeader);
    
    {
        uint16_t stringSize = message.messageHeader->pathSizeBytes;
        if (stringSize > 0)
        {
            assert(bufferBytesRemain >= stringSize);
            memcpy(bufferPosition, message.path, stringSize);
            bufferPosition += stringSize;
            bufferBytesRemain -= stringSize;
        }
    }

    {
        uint16_t stringSize = message.messageHeader->fromPathSizeBytes;
        if (stringSize > 0)
        {
            assert(bufferBytesRemain >= stringSize);
            memcpy(bufferPosition, message.fromPath, stringSize);
            bufferPosition += stringSize;
            bufferBytesRemain -= stringSize;
        }
    }

    return bufferSize - bufferBytesRemain;
}
