#include "ProviderMessaging.hpp"
#include "Locks.hpp"
#include "KextLog.hpp"

#include <kern/assert.h>
#include <sys/proc.h>
#include <libkern/OSAtomic.h>
#include <sys/kauth.h>

// Structs
struct OutstandingMessage
{
    MessageHeader                  request;
    MessageType                    result;
    bool                           receivedResult;
    VirtualizationRootHandle       rootHandle;
    
    LIST_ENTRY(OutstandingMessage) _list_privates;
    
};

// State
static LIST_HEAD(OutstandingMessage_Head, OutstandingMessage) s_outstandingMessages = LIST_HEAD_INITIALIZER(OutstandingMessage_Head);
static Mutex s_outstandingMessagesMutex = {};
static volatile int s_nextMessageId;
static volatile bool s_isShuttingDown;


bool ProviderMessaging_Init()
{
    LIST_INIT(&s_outstandingMessages);
    s_nextMessageId = 1;
    
    s_isShuttingDown = false;
    
    s_outstandingMessagesMutex = Mutex_Alloc();
    if (!Mutex_IsValid(s_outstandingMessagesMutex))
    {
        goto CleanupAndFail;
    }
        
    return true;

CleanupAndFail:
    ProviderMessaging_Cleanup();
    return false;
}


void ProviderMessaging_Cleanup()
{
    if (Mutex_IsValid(s_outstandingMessagesMutex))
    {
        Mutex_FreeMemory(&s_outstandingMessagesMutex);
    }
}


void ProviderMessaging_HandleKernelMessageResponse(VirtualizationRootHandle providerVirtualizationRootHandle, uint64_t messageId, MessageType responseType)
{
    switch (responseType)
    {
        case MessageType_Response_Success:
        case MessageType_Response_Fail:
        {
            Mutex_Acquire(s_outstandingMessagesMutex);
            {
                OutstandingMessage* outstandingMessage;
                LIST_FOREACH(outstandingMessage, &s_outstandingMessages, _list_privates)
                {
                    if (outstandingMessage->request.messageId == messageId && outstandingMessage->rootHandle == providerVirtualizationRootHandle)
                    {
                        // Save the response for the blocked thread.
                        outstandingMessage->result = responseType;
                        outstandingMessage->receivedResult = true;
                        
                        wakeup(outstandingMessage);
                        
                        break;
                    }
                }
            }
            Mutex_Release(s_outstandingMessagesMutex);
            break;
        }
        
        // The follow are not valid responses to kernel messages
        case MessageType_Invalid:
        case MessageType_KtoU_EnumerateDirectory:
        case MessageType_KtoU_RecursivelyEnumerateDirectory:
        case MessageType_KtoU_HydrateFile:
        case MessageType_KtoU_NotifyFileModified:
        case MessageType_KtoU_NotifyFilePreDelete:
        case MessageType_KtoU_NotifyDirectoryPreDelete:
        case MessageType_KtoU_NotifyFileCreated:
        case MessageType_KtoU_NotifyFileRenamed:
        case MessageType_KtoU_NotifyDirectoryRenamed:
        case MessageType_KtoU_NotifyFileHardLinkCreated:
        case MessageType_Result_Aborted:
        default:
            KextLog_Error("KauthHandler_HandleKernelMessageResponse: Unexpected responseType: %d", responseType);
            break;
    }
    
    return;
}

void ProviderMessaging_AbortOutstandingEventsForProvider(VirtualizationRootHandle providerVirtualizationRootHandle)
{
    // Mark all outstanding messages for this root as aborted and wake up the waiting threads
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        OutstandingMessage* outstandingMessage;
        LIST_FOREACH(outstandingMessage, &s_outstandingMessages, _list_privates)
        {
            if (outstandingMessage->rootHandle == providerVirtualizationRootHandle)
            {
                outstandingMessage->receivedResult = true;
                outstandingMessage->result = MessageType_Result_Aborted;
                wakeup(outstandingMessage);
            }
        }
    }
    Mutex_Release(s_outstandingMessagesMutex);
}

#ifndef KEXT_UNIT_TESTING
bool ProviderMessaging_TrySendRequestAndWaitForResponse(
    VirtualizationRootHandle root,
    MessageType messageType,
    const vnode_t vnode,
    const FsidInode& vnodeFsidInode,
    const char* vnodePath,
    int pid,
    const char* procname,
    int* kauthResult,
    int* kauthError)
{
    // To be useful, the message needs to either provide an FSID/inode pair or a path
    assert(vnodePath != nullptr || (vnodeFsidInode.fsid.val[0] != 0 || vnodeFsidInode.fsid.val[1] != 0));
    bool result = false;
    const char* relativePath = nullptr;
    
    OutstandingMessage message =
    {
        .receivedResult = false,
        .rootHandle = root,
    };
    
    if (nullptr != vnodePath)
    {
        relativePath = VirtualizationRoot_GetRootRelativePath(root, vnodePath);
    }
    
    int nextMessageId = OSIncrementAtomic(&s_nextMessageId);
    
    Message messageSpec = {};
    Message_Init(
        &messageSpec,
        &(message.request),
        nextMessageId,
        messageType,
        vnodeFsidInode,
        pid,
        procname,
        relativePath);

    if (s_isShuttingDown)
    {
        return false;
    }
    
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        LIST_INSERT_HEAD(&s_outstandingMessages, &message, _list_privates);
    }
    Mutex_Release(s_outstandingMessagesMutex);
    
    errno_t sendError = ActiveProvider_SendMessage(root, messageSpec);
   
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        if (0 != sendError)
        {
            // TODO: appropriately handle unresponsive providers
            *kauthResult = KAUTH_RESULT_DEFER;
        }
        else
        {
            while (!message.receivedResult &&
                   !s_isShuttingDown)
            {
                Mutex_Sleep(5, &message, &s_outstandingMessagesMutex);
            }
        
            if (s_isShuttingDown)
            {
                *kauthResult = KAUTH_RESULT_DENY;
            }
            else if (MessageType_Response_Success == message.result)
            {
                *kauthResult = KAUTH_RESULT_DEFER;
                result = true;
            }
            else
            {
                // Default error code is EACCES. See errno.h for more codes.
                *kauthError = EAGAIN;
                *kauthResult = KAUTH_RESULT_DENY;
            }
        }
        
        LIST_REMOVE(&message, _list_privates);
    }
    Mutex_Release(s_outstandingMessagesMutex);
    
    return result;
}
#endif

void ProviderMessaging_AbortAllOutstandingEvents()
{
    // Wake up all sleeping threads so they can see that that we're shutting down and return an error
    Mutex_Acquire(s_outstandingMessagesMutex);
    {
        s_isShuttingDown = true;
        
        OutstandingMessage* outstandingMessage;
        LIST_FOREACH(outstandingMessage, &s_outstandingMessages, _list_privates)
        {
            wakeup(outstandingMessage);
        }
    }
    Mutex_Release(s_outstandingMessagesMutex);
}
