#pragma once

// External method selectors for provider user clients
enum PrjFSProviderUserClientSelector
{
    ProviderSelector_Invalid = 0,
    
    ProviderSelector_RegisterVirtualizationRootPath,
    ProviderSelector_KernelMessageResponse,
};

enum PrjFSProviderUserClientMemoryType
{
    ProviderMemoryType_Invalid = 0,
    
    ProviderMemoryType_MessageQueue,
};

enum PrjFSProviderUserClientPortType
{
    ProviderPortType_Invalid = 0,
    
    ProviderPortType_MessageQueue,
};
