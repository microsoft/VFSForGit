#pragma once

#include "PrjFSClasses.hpp"
#include <IOKit/IOUserClient.h>

class PrjFSOfflineIOUserClient : public IOUserClient
{
    OSDeclareDefaultStructors(PrjFSOfflineIOUserClient);
private:
    typedef IOUserClient super;
    pid_t pid;

public:
    // IOUserClient methods:
    virtual bool initWithTask(task_t owningTask, void* securityToken, UInt32 type, OSDictionary* properties) override;
    virtual IOReturn clientClose() override;

    // IOService methods:
    virtual void stop(IOService* provider) override;
};

