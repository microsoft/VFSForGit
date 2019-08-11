#include "public/Message.h"

#ifdef KERNEL
#include <kern/assert.h>
#else
#include <cassert>
#endif

uint32_t Message_EncodedSize(const MessageHeader* messageHeader)
{
    uint32_t size = sizeof(*messageHeader);
    for (unsigned i = 0; i < MessagePath_Count; ++i)
    {
        bool overflow = __builtin_uadd_overflow(messageHeader->pathSizesBytes[i], size, &size);
        assert(!overflow); // small constant plus small number of uint16_t should never overflow a uint32_t
    }
    return size;
}

const char* Message_MessageTypeString(MessageType messageType)
{
    switch(messageType)
    {
        case MessageType_KtoU_EnumerateDirectory:            return "EnumerateDirectory";
        case MessageType_KtoU_RecursivelyEnumerateDirectory: return "RecursivelyEnumerateDirectory";
        case MessageType_KtoU_HydrateFile:                   return "HydrateFile";
        case MessageType_KtoU_NotifyFileModified:            return "NotifyFileModified";
        case MessageType_KtoU_NotifyFilePreDelete:           return "NotifyFilePreDelete";
        case MessageType_KtoU_NotifyFilePreDeleteFromRename: return "NotifyFilePreDeleteFromRename";
        case MessageType_KtoU_NotifyDirectoryPreDelete:      return "NotifyDirectoryPreDelete";
        case MessageType_KtoU_NotifyFileCreated:             return "NotifyFileCreated";
        case MessageType_KtoU_NotifyFileRenamed:             return "NotifyFileRenamed";
        case MessageType_KtoU_NotifyDirectoryRenamed:        return "NotifyDirectoryRenamed";
        case MessageType_KtoU_NotifyFileHardLinkCreated:     return "NotifyFileHardLinkCreated";
        case MessageType_KtoU_NotifyFilePreConvertToFull:    return "NotifyFilePreConvertToFull";
        default:                                             return "Unknown";
    };
}
