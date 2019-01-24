#pragma once

#include "PrjFSClasses.hpp"
#include <IOKit/IOService.h>

class PrjFSService : public IOService
{
    OSDeclareDefaultStructors(PrjFSService);
private:
    typedef IOService super;
public:
    // IOService overrides:
    virtual bool start(IOService* provider) override;
    virtual void stop(IOService* provider) override;
    virtual IOReturn newUserClient(
        task_t owningTask, void* securityID, UInt32 type,
        OSDictionary* properties, IOUserClient** handler ) override;
};
