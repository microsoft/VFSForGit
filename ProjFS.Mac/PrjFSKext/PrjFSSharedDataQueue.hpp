#pragma once

#include "PrjFSClasses.hpp"
#include <IOKit/IOSharedDataQueue.h>

class PrjFSSharedDataQueue : public IOSharedDataQueue
{
    OSDeclareDefaultStructors(PrjFSSharedDataQueue);
public:
    virtual Boolean enqueue(void* data, UInt32 dataSize) override;

};
