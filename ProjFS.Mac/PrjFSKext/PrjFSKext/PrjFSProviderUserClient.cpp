#include "PrjFSProviderUserClient.hpp"
#include "PrjFSCommon.h"
#include "../public/PrjFSProviderClientShared.h"
#include "Message.h"
#include "KauthHandler.hpp"
#include "VirtualizationRoots.hpp"

#include <IOKit/IOSharedDataQueue.h>
#include <sys/proc.h>

OSDefineMetaClassAndStructors(PrjFSProviderUserClient, IOUserClient);

// Amount of memory to set aside for kernel -> userspace messages.
// Should be chosen to comfortably hold "enough" Message structs and associated path strings.
static const uint32_t ProviderMessageQueueCapacityBytes = 100 * 1024;


static const IOExternalMethodDispatch ProviderUserClientDispatch[] =
{
    [ProviderSelector_RegisterVirtualizationRootPath] =
        {
            .function =                 &PrjFSProviderUserClient::registerVirtualizationRoot,
            .checkScalarInputCount =    0,
            .checkStructureInputSize =  kIOUCVariableStructureSize, // null-terminated string: virtualisation root path
            .checkScalarOutputCount =   1, // returned errno
            .checkStructureOutputSize = 0
        },
    [ProviderSelector_KernelMessageResponse] =
        {
            .function =                 &PrjFSProviderUserClient::kernelMessageResponse,
            .checkScalarInputCount =    2, // message id, response type
            .checkStructureInputSize =  0,
            .checkScalarOutputCount =   0,
            .checkStructureOutputSize = 0
        },
};

bool PrjFSProviderUserClient::initWithTask(
    task_t owningTask,
    void* securityToken,
    UInt32 type,
    OSDictionary* properties)
{
    this->virtualizationRootHandle = RootHandle_None;
    this->pid = proc_selfpid();

    if (!this->super::initWithTask(owningTask, securityToken, type, properties))
    {
        return false;
    }
    
    this->dataQueueWriterMutex = Mutex_Alloc();
    if (!Mutex_IsValid(this->dataQueueWriterMutex))
    {
        goto CleanupAndFail;
    }
    
    this->dataQueue = IOSharedDataQueue::withCapacity(ProviderMessageQueueCapacityBytes);
    if (nullptr == this->dataQueue)
    {
        goto CleanupAndFail;
    }
    
    this->dataQueueMemory = this->dataQueue->getMemoryDescriptor();
    if (nullptr == this->dataQueueMemory)
    {
        goto CleanupAndFail;
    }
    
    return true;
    
CleanupAndFail:
    if (Mutex_IsValid(this->dataQueueWriterMutex))
    {
        Mutex_FreeMemory(&this->dataQueueWriterMutex);
    }
    
    OSSafeReleaseNULL(this->dataQueueMemory);
    OSSafeReleaseNULL(this->dataQueue);
    return false;
}

void PrjFSProviderUserClient::free()
{
    OSSafeReleaseNULL(this->dataQueueMemory);
    OSSafeReleaseNULL(this->dataQueue);
    if (Mutex_IsValid(this->dataQueueWriterMutex))
    {
        Mutex_FreeMemory(&this->dataQueueWriterMutex);
    }
    
    this->super::free();
}

// Called when the user process explicitly or implicitly (process death) closes
// the connection.
IOReturn PrjFSProviderUserClient::clientClose()
{
    VirtualizationRootHandle root = this->virtualizationRootHandle;
    this->virtualizationRootHandle = RootHandle_None;
    if (RootHandle_None != root)
    {
        ActiveProvider_Disconnect(root);
    }
    
    this->terminate(0);
    return kIOReturnSuccess;
}

// Called when user process requests memory-mapping of kernel data
// structures via IOConnectMapMemory64().
IOReturn PrjFSProviderUserClient::clientMemoryForType(
    UInt32 type,
    IOOptionBits* options,
    IOMemoryDescriptor** memory)
{
    switch (type)
    {
    case ProviderMemoryType_MessageQueue:
        {
            IOMemoryDescriptor* queueMemory;
            
            Mutex_Acquire(this->dataQueueWriterMutex);
            {
                queueMemory = this->dataQueueMemory;
                if (queueMemory != nullptr)
                {
                    queueMemory->retain(); // Matched internally in IOUserClient
                }
            }
            Mutex_Release(this->dataQueueWriterMutex);
            
            *memory = queueMemory;
            return nullptr == queueMemory ? kIOReturnError : kIOReturnSuccess;
        }
        break;
    }
    
    return kIOReturnError;
}

IOReturn PrjFSProviderUserClient::registerNotificationPort(
    mach_port_t port,
    UInt32 type,
    io_user_reference_t refCon)
{
    if (type == ProviderPortType_MessageQueue)
    {
        if(port == MACH_PORT_NULL)
        {
            return kIOReturnError;
        }

        Mutex_Acquire(this->dataQueueWriterMutex);
        {
            assert(nullptr != this->dataQueue);
            this->dataQueue->setNotificationPort(port);
        }
        Mutex_Release(this->dataQueueWriterMutex);
        
        return kIOReturnSuccess;
    }
    else
    {
        return this->super::registerNotificationPort(port, type, refCon);
    }
}

IOReturn PrjFSProviderUserClient::externalMethod(
    uint32_t selector,
    IOExternalMethodArguments* arguments,
    IOExternalMethodDispatch* dispatch,
    OSObject* target,
    void* reference)
{
    IOExternalMethodDispatch local_dispatch = {};
    if (selector < sizeof(ProviderUserClientDispatch) / sizeof(ProviderUserClientDispatch[0]))
    {
        if (nullptr != ProviderUserClientDispatch[selector].function)
        {
            local_dispatch = ProviderUserClientDispatch[selector];
            dispatch = &local_dispatch;
            target = this;
        }
    }
    return this->super::externalMethod(selector, arguments, dispatch, target, reference);
}

IOReturn PrjFSProviderUserClient::kernelMessageResponse(
    OSObject* target,
    void* reference,
    IOExternalMethodArguments* arguments)
{
    return static_cast<PrjFSProviderUserClient*>(target)->kernelMessageResponse(
        arguments->scalarInput[0],
        static_cast<MessageType>(arguments->scalarInput[1]));
}

IOReturn PrjFSProviderUserClient::kernelMessageResponse(uint64_t messageId, MessageType responseType)
{
    KauthHandler_HandleKernelMessageResponse(this->virtualizationRootHandle, messageId, responseType);
    return kIOReturnSuccess;
}

IOReturn PrjFSProviderUserClient::registerVirtualizationRoot(
    OSObject* target,
    void* reference,
    IOExternalMethodArguments* arguments)
{
    // We don't support larger strings, including those large enough to warrant a memory descriptor
    if (arguments->structureInputSize > PrjFSMaxPath || nullptr == arguments->structureInput)
    {
        return kIOReturnBadArgument;
    }
    
    return static_cast<PrjFSProviderUserClient*>(target)->registerVirtualizationRoot(
        static_cast<const char*>(arguments->structureInput),
        arguments->structureInputSize,
        &arguments->scalarOutput[0]);
}

IOReturn PrjFSProviderUserClient::registerVirtualizationRoot(const char* rootPath, size_t rootPathSize, uint64_t* outError)
{
    if (rootPathSize == 0 || strnlen(rootPath, rootPathSize) != rootPathSize - 1)
    {
        *outError = EINVAL;
        return kIOReturnSuccess;
    }
    else if (this->virtualizationRootHandle != RootHandle_None)
    {
        // Already set
        *outError = EBUSY;
        return kIOReturnSuccess;
    }
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(this, this->pid, rootPath);
    if (0 == result.error)
    {
        this->virtualizationRootHandle = result.root;

        // Sets the root index in the IORegistry for diagnostic purposes
        char location[5] = "";
        snprintf(location, sizeof(location), "%d", result.root);
        this->setLocation(location);
    }
    
    *outError = result.error;
    
    return kIOReturnSuccess;
}

void PrjFSProviderUserClient::sendMessage(const void* message, uint32_t size)
{
    Mutex_Acquire(this->dataQueueWriterMutex);
    {
        // IOSharedDataQueue::enqueue() only reads (memcpy source), but doesn't take a const pointer for some reason
        bool ok = this->dataQueue->enqueue(const_cast<void*>(message), size);
        assert(ok);

        // TODO: block here and try again when space has cleared if enqueueing fails
    }
    Mutex_Release(this->dataQueueWriterMutex);
}

