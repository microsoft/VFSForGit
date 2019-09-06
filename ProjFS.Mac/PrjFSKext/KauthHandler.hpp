#ifndef KauthHandler_h
#define KauthHandler_h

#include "public/Message.h"
#include "VirtualizationRoots.hpp"

kern_return_t KauthHandler_Init();
kern_return_t KauthHandler_Cleanup();

bool KauthHandler_IsFileSystemCrawler(const char* procname);

#endif /* KauthHandler_h */
