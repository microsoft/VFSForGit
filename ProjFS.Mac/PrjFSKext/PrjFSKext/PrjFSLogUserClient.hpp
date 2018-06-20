#pragma once

#include "PrjFSClasses.hpp"
#include "Locks.hpp"
#include "PrjFSCommon.h"
#include <IOKit/IOUserClient.h>

class IOSharedDataQueue;
struct KextLog_MessageHeader;

class PrjFSLogUserClient : public IOUserClient
{
    OSDeclareDefaultStructors(PrjFSLogUserClient);
private:
    typedef IOUserClient super;
    IOSharedDataQueue* dataQueue;
    IOMemoryDescriptor* dataQueueMemory;
    Mutex dataQueueWriterMutex;
    bool logMessageDropped;
    void cleanUp();
public:
    virtual bool initWithTask(task_t owningTask, void* securityToken, UInt32 type, OSDictionary* properties) override;
    
    virtual void free() override;
    virtual IOReturn clientClose() override;
    virtual IOReturn clientMemoryForType(UInt32 type, IOOptionBits* options, IOMemoryDescriptor** memory) override;
    virtual IOReturn registerNotificationPort(mach_port_t port, UInt32 type, io_user_reference_t refCon) override;
    
    virtual IOReturn externalMethod(
        uint32_t selector,
        IOExternalMethodArguments* arguments,
        IOExternalMethodDispatch* dispatch = nullptr,
        OSObject* target = nullptr,
        void* reference = nullptr) override;

    
    static IOReturn fetchProfilingData(
        OSObject* target,
        void* reference,
        IOExternalMethodArguments* arguments);
    
    void sendLogMessage(KextLog_MessageHeader* message, uint32_t size);
};
