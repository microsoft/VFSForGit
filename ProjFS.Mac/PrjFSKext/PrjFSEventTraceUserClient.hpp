#pragma once

#include "PrjFSClasses.hpp"
#include "Locks.hpp"
#include "public/PrjFSCommon.h"
#include <IOKit/IOUserClient.h>

class IOSharedDataQueue;
struct KextLog_MessageHeader;

class PrjFSEventTraceUserClient : public IOUserClient
{
    OSDeclareDefaultStructors(PrjFSEventTraceUserClient);
private:
    typedef IOUserClient super;
public:
    // External methods:
    static IOReturn setEventTracingMode(
        OSObject* target,
        void* reference,
        IOExternalMethodArguments* arguments);

    // IOUserClient methods:
    virtual bool initWithTask(task_t owningTask, void* securityToken, UInt32 type, OSDictionary* properties) override;
    
    virtual IOReturn clientClose() override;

    virtual IOReturn externalMethod(
        uint32_t selector,
        IOExternalMethodArguments* arguments,
        IOExternalMethodDispatch* dispatch = nullptr,
        OSObject* target = nullptr,
        void* reference = nullptr) override;

    // IOService methods:
    virtual void stop(IOService* provider) override;
};
