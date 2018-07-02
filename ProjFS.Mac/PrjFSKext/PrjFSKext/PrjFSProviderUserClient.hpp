#pragma once

#include "PrjFSClasses.hpp"
#include "Locks.hpp"
#include "Message.h"
#include <IOKit/IOUserClient.h>

struct MessageHeader;
struct VirtualizationRoot;
class IOSharedDataQueue;
class PrjFSProviderUserClient : public IOUserClient
{
    OSDeclareDefaultStructors(PrjFSProviderUserClient);
private:
    typedef IOUserClient super;
    IOSharedDataQueue* dataQueue;
    IOMemoryDescriptor* dataQueueMemory;
    Mutex dataQueueWriterMutex;
public:
    pid_t pid;
    // The root for which this is the provider; -1 prior to registration
    int32_t virtualizationRootIndex;
    
    // IOUserClient methods:
    virtual bool initWithTask(
        task_t owningTask, void* securityToken, UInt32 type,
        OSDictionary* properties) override;
    
    virtual IOReturn externalMethod(
        uint32_t selector,
        IOExternalMethodArguments* arguments,
        IOExternalMethodDispatch* dispatch = 0,
        OSObject* target = 0,
        void* reference = 0) override;
    virtual IOReturn clientMemoryForType(
        UInt32 type,
        IOOptionBits* options,
        IOMemoryDescriptor** memory) override;
    virtual IOReturn registerNotificationPort(
        mach_port_t port,
        UInt32 type,
        io_user_reference_t refCon) override;
    
    virtual IOReturn clientClose() override;

    // OSObject methods:
    virtual void free() override;


    void sendMessage(const void* message, uint32_t size);

    // External methods:
    static IOReturn registerVirtualizationRoot(
        OSObject* target,
        void* reference,
        IOExternalMethodArguments* arguments);
    IOReturn registerVirtualizationRoot(const char* rootPath, size_t rootPathSize, uint64_t* outError);

    static IOReturn kernelMessageResponse(
        OSObject* target,
        void* reference,
        IOExternalMethodArguments* arguments);
    IOReturn kernelMessageResponse(uint64_t messageId, MessageType responseType);
};
