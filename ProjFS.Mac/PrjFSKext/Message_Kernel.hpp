#pragma once

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
    const char* fromPath);

uint32_t Message_Encode(void* buffer, uint32_t bufferSize, const Message& message);

