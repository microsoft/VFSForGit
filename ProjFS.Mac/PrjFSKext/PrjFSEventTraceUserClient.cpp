#include "PrjFSEventTraceUserClient.hpp"
#include "public/PrjFSEventTraceClientShared.h"
#include "UserClientUtilities.hpp"
#include "KauthHandler.hpp"
#include "KextLog.hpp"
#include <sys/syslimits.h>

OSDefineMetaClassAndStructors(PrjFSEventTraceUserClient, IOUserClient);


static const IOExternalMethodDispatch EventTraceClientDispatch[] =
{
    [EventTraceSelector_SetEventTracingMode] =
        {
            .function =                 &PrjFSEventTraceUserClient::setEventTracingMode,
            // 0: event filtering flags (EventTraceFilterFlags)
            // 1: kauth vnode action filter mask
            .checkScalarInputCount =    2,
            .checkStructureInputSize =  kIOUCVariableStructureSize, // path prefix filter string
            .checkScalarOutputCount =   0,
            .checkStructureOutputSize = 0,
        },
};


IOReturn PrjFSEventTraceUserClient::externalMethod(
    uint32_t selector,
    IOExternalMethodArguments* arguments,
    IOExternalMethodDispatch* dispatch,
    OSObject* target,
    void* reference)
{
    IOExternalMethodDispatch local_dispatch = {};
    UserClient_ExternalMethodDispatch(this, EventTraceClientDispatch, local_dispatch, selector, dispatch, target);
    return this->super::externalMethod(selector, arguments, dispatch, target, reference);
}

bool PrjFSEventTraceUserClient::initWithTask(task_t owningTask, void* securityToken, UInt32 type, OSDictionary* properties)
{
    if (!this->super::initWithTask(owningTask, securityToken, type, properties))
    {
        return false;
    }
    
    // Only allow root user to trace events
    if (kIOReturnSuccess != IOUserClient::clientHasPrivilege(securityToken, kIOClientPrivilegeAdministrator))
    {
        return false;
    }
    
    return true;
}

void PrjFSEventTraceUserClient::stop(IOService* provider)
{
    // Disable any active trace
    KauthHandlerEventTracingSettings traceSettings = {};
    KauthHandler_EnableTraceListeners(false, traceSettings);

    this->super::stop(provider);
}

IOReturn PrjFSEventTraceUserClient::clientClose()
{
    this->terminate(0);
    return kIOReturnSuccess;
}

IOReturn PrjFSEventTraceUserClient::setEventTracingMode(
    OSObject* target,
    void* reference,
    IOExternalMethodArguments* arguments)
{
    // We don't support larger strings, including those large enough to warrant a memory descriptor
    if (arguments->structureInputSize > PATH_MAX || nullptr == arguments->structureInput)
    {
        return kIOReturnBadArgument;
    }
    
    const char* pathPrefixFilter = static_cast<const char*>(arguments->structureInput);
    size_t pathPrefixFilterLength = strnlen(pathPrefixFilter, arguments->structureInputSize);
    if (pathPrefixFilterLength != arguments->structureInputSize - 1u)
    {
        KextLog_Error("PrjFSEventTraceUserClient::setEventTracingMode: bad path prefix filter. Got %u bytes, string length %zu, expect string to fill buffer plus 1 null byte.", arguments->structureInputSize, pathPrefixFilterLength);
        return kIOReturnBadArgument;
    }

    uint64_t traceFlags = arguments->scalarInput[0];
    KauthHandlerEventTracingSettings traceSettings =
        {
            .pathPrefixFilter = pathPrefixFilter,
            .vnodeActionFilterMask = static_cast<kauth_action_t>(arguments->scalarInput[1]),
            .traceDeniedVnodeEvents =            (0 != (traceFlags & EventTraceFilter_Vnode_Denied)),
            .traceProviderMessagingVnodeEvents = (0 != (traceFlags & EventTraceFilter_Vnode_ProviderMessage)),
            .traceAllVnodeEvents =               (0 != (traceFlags & EventTraceFilter_Vnode_All)),
            .traceCrawlerEvents =                (0 != (traceFlags & EventTraceFilter_Vnode_Crawler)),
        };
    
    KauthHandler_EnableTraceListeners(traceFlags != 0, traceSettings);
    return kIOReturnSuccess;
}
