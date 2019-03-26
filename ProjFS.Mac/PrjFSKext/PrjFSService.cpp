#include "public/PrjFSCommon.h"
#include "public/PrjFSProviderClientShared.h"
#include "PrjFSService.hpp"
#include "PrjFSProviderUserClientPrivate.hpp"
#include "PrjFSLogUserClient.hpp"
#include "KextLog.hpp"
#include "VirtualizationRoots.hpp"

#include <IOKit/IOLib.h>
#include <libkern/OSAtomic.h>
#include <kern/assert.h>
#include <sys/proc.h>

OSDefineMetaClassAndStructors(PrjFSService, IOService);

// We really only want one instance of this class
static PrjFSService* service_singleton = nullptr;

bool PrjFSService::start(IOService* provider)
{
    bool ok = this->super::start(provider);
    if (!ok)
    {
        return false;
    }
    
    // Protect agaist multiple instances being created
    if (!OSCompareAndSwapPtr(nullptr, this, &service_singleton))
    {
        return false;
    }

    // Perform one-off initialisation here:
    
    
    // Set a more readable name for us to find in IORegistry
    this->setName("PrjFS");
    
    OSString* kextVersion = OSString::withCString(PrjFSKextVersion);
    this->setProperty(PrjFSKextVersionKey, kextVersion);
    OSSafeReleaseNULL(kextVersion);
    
    // Publishes the service, ready for matching
    this->registerService();
    
    return true;
}

void PrjFSService::stop(IOService* provider)
{
    // Perform one-off shutdown here:


    if (!OSCompareAndSwapPtr(this, nullptr, &service_singleton))
    {
        KextLog_Error("PrjFSService::stop: Warning: failed to deregister PrjFS singleton service. Bug?\n");
    }
    this->super::stop(provider);
}

static bool InitAttachAndStartUserClient(
    PrjFSService* service, IOUserClient* client, task_t owningTask,
    void* securityID, UInt32 type, OSDictionary* properties)
{
    if (nullptr == client)
    {
        return false;
    }
    
    if (client->initWithTask(owningTask, securityID, type, properties))
    {
        if (client->attach(service) && client->start(service))
        {
            return true;
        }
        client->detach(service);
    }
    
    client->release();
    return false;
}

static void StopDetachReleaseUserClient(IOService* service, IOUserClient* client)
{
    client->stop(service);
    client->detach(service);
    client->release();
}

IOReturn PrjFSService::newUserClient(
    task_t owningTask,
    void* securityID,
    UInt32 type,
    OSDictionary* properties,
    IOUserClient** handler)
{
    IOReturn result = kIOReturnUnsupported;
    switch (type)
    {
    case UserClientType_Provider:
        {
            PrjFSProviderUserClient* provider_client = new PrjFSProviderUserClient();
            if (InitAttachAndStartUserClient(this, provider_client, owningTask, securityID, type, properties))
            {
                *handler = provider_client;
                result = kIOReturnSuccess;
            }
        }
        break;
    case UserClientType_Log:
        {
            PrjFSLogUserClient* log_client = new PrjFSLogUserClient();
            if (InitAttachAndStartUserClient(this, log_client, owningTask, securityID, type, properties))
            {
                if (KextLog_RegisterUserClient(log_client))
                {
                    *handler = log_client;
                    result = kIOReturnSuccess;
                }
                else
                {
                    StopDetachReleaseUserClient(this, log_client);
                    result = kIOReturnExclusiveAccess;
                }
            }
        }
        break;
    }

    return result;
}
