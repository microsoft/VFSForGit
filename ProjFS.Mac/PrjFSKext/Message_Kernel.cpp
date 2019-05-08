#include <kern/debug.h>
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
