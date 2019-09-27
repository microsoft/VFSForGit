#include "PrjFSOfflineIOUserClient.hpp"
#include "VirtualizationRoots.hpp"
#include <sys/proc.h>

OSDefineMetaClassAndStructors(PrjFSOfflineIOUserClient, IOUserClient);

bool PrjFSOfflineIOUserClient::initWithTask(
    task_t owningTask, void* securityToken, UInt32 type, OSDictionary* properties)
{
    if (!this->super::initWithTask(owningTask, securityToken, type, properties))
    {
        return false;
    }
    
    this->pid = proc_selfpid();
    bool success = VirtualizationRoots_AddOfflineIOProcess(this->pid);
    if (!success)
    {
        this->pid = 0;
    }
    
    return success;
}

IOReturn PrjFSOfflineIOUserClient::clientClose()
{
    this->terminate(0);
    return kIOReturnSuccess;
}

void PrjFSOfflineIOUserClient::stop(IOService* provider)
{
    if (this->pid != 0)
    {
        VirtualizationRoots_RemoveOfflineIOProcess(this->pid);
        this->pid = 0;
    }
    
    this->super::stop(provider);
}

