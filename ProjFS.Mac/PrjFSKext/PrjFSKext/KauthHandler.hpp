#ifndef KauthHandler_h
#define KauthHandler_h

#include "Message.h"

kern_return_t KauthHandler_Init();
kern_return_t KauthHandler_Cleanup();

void KauthHandler_HandleKernelMessageResponse(uint64_t messageId, MessageType responseType);

#endif /* KauthHandler_h */
