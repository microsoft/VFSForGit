#pragma once

#include <IOKit/IOUserClient.h>

template <size_t DISPATCH_SIZE>
void UserClient_ExternalMethodDispatch(
    IOUserClient* self,
    const IOExternalMethodDispatch (&dispatchTable)[DISPATCH_SIZE],
    IOExternalMethodDispatch& local_dispatch,
    uint32_t selector,
    IOExternalMethodDispatch*& dispatch,
    OSObject*& target)
{
    if (selector < DISPATCH_SIZE)
    {
        if (nullptr != dispatchTable[selector].function)
        {
            local_dispatch = dispatchTable[selector];
            dispatch = &local_dispatch;
            target = self;
        }
    }
}
