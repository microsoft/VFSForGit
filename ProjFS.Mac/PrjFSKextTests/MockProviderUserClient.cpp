#include "MockProviderUserClient.hpp"
#include "KextMockUtilities.hpp"

void ProviderUserClient_Retain(PrjFSProviderUserClient* userClient)
{

}

void ProviderUserClient_Release(PrjFSProviderUserClient* userClient)
{
}

void ProviderUserClient_SendMessage(PrjFSProviderUserClient* userClient, const void* message, uint32_t size)
{
    MockCalls::RecordFunctionCall(ProviderUserClient_SendMessage, userClient, message, size);
}

void ProviderUserClient_UpdatePathProperty(PrjFSProviderUserClient* userClient, const char* providerPath)
{
    MockCalls::RecordFunctionCall(ProviderUserClient_UpdatePathProperty, userClient, providerPath);
}
