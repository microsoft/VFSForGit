#pragma once

#include "PrjFSClasses.hpp"
#include <stdint.h>

void ProviderUserClient_UpdatePathProperty(PrjFSProviderUserClient* userClient, const char* providerPath);
void ProviderUserClient_Retain(PrjFSProviderUserClient* userClient);
void ProviderUserClient_Release(PrjFSProviderUserClient* userClient);
void ProviderUserClient_SendMessage(PrjFSProviderUserClient* userClient, const void* message, uint32_t size);
