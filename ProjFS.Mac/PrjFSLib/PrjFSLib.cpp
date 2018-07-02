#include <iostream>
#include <cassert>
#include <stddef.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/sys_domain.h>
#include <sys/xattr.h>
#include <thread>
#include <unistd.h>
#include <unordered_map>
#include <set>
#include <IOKit/IOKitLib.h>
#include <IOKit/IODataQueueClient.h>
#include <mach/mach_port.h>
#include <CoreFoundation/CFNumber.h>

#include "stdlib.h"

#include "PrjFSLib.h"
#include "PrjFSKext/public/PrjFSCommon.h"
#include "PrjFSKext/public/PrjFSXattrs.h"
#include "PrjFSKext/public/Message.h"
#include "PrjFSUser.hpp"

using std::endl; using std::cerr;
using std::unordered_map; using std::set; using std::string;
using std::mutex;
typedef std::lock_guard<mutex> mutex_lock;

// Structs
struct _PrjFS_FileHandle
{
    FILE* file;
};

// Function prototypes
static bool SetBitInFileFlags(const char* path, uint32_t bit, bool value);
static bool IsBitSetInFileFlags(const char* path, uint32_t bit);

static bool InitializeEmptyPlaceholder(const char* fullPath);
template<typename TPlaceholder> static bool InitializeEmptyPlaceholder(const char* fullPath, TPlaceholder* data, const char* xattrName);
static bool AddXAttr(const char* path, const char* name, const void* value, size_t size);
static bool GetXAttr(const char* path, const char* name, size_t size, _Out_ void* value);

static bool IsVirtualizationRoot(const char* path);
static void CombinePaths(const char* root, const char* relative, char (&combined)[PrjFSMaxPath]);

static errno_t SendKernelMessageResponse(uint64_t messageId, MessageType responseType);
static errno_t RegisterVirtualizationRootPath(const char* path);

static void HandleKernelRequest(Message requestSpec, void* messageMemory);
static PrjFS_Result HandleEnumerateDirectoryRequest(const MessageHeader* request, const char* path);
static PrjFS_Result HandleHydrateFileRequest(const MessageHeader* request, const char* path);

static Message ParseMessageMemory(const void* messageMemory, uint32_t size);

static void ClearMachNotification(mach_port_t port);

// State
static io_connect_t s_kernelServiceConnection = IO_OBJECT_NULL;
static std::string s_virtualizationRootFullPath;
static PrjFS_Callbacks s_callbacks;
static dispatch_queue_t s_messageQueueDispatchQueue;
static dispatch_queue_t s_kernelRequestHandlingConcurrentQueue;

// Map of relative path -> set of pending message IDs for that path, plus mutex to protect it.
static unordered_map<string, set<uint64_t>> s_PendingRequestMessageIDs;
static std::mutex s_PendingRequestMessageMutex;


// The full API is defined in the header, but only the minimal set of functions needed
// for the initial MirrorProvider implementation are listed here. Calling any other function
// will lead to a linker error for now.

// Public functions

PrjFS_Result PrjFS_StartVirtualizationInstance(
    _In_    const char*                             virtualizationRootFullPath,
    _In_    PrjFS_Callbacks                         callbacks,
    _In_    unsigned int                            poolThreadCount)
{
#ifdef DEBUG
    std::cout
        << "PrjFS_StartVirtualizationInstance("
        << virtualizationRootFullPath << ", "
        << callbacks.EnumerateDirectory << ", "
        << callbacks.GetFileStream << ", "
        << callbacks.NotifyOperation << ", "
        << poolThreadCount << ")" << std::endl;
#endif
    
    if (nullptr == virtualizationRootFullPath ||
        nullptr == callbacks.EnumerateDirectory ||
        nullptr == callbacks.GetFileStream)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    if (nullptr != callbacks.NotifyOperation)
    {
        return PrjFS_Result_ENotSupported;
    }
    
    if (!s_virtualizationRootFullPath.empty())
    {
        return PrjFS_Result_EInvalidOperation;
    }
    
    if (!IsVirtualizationRoot(virtualizationRootFullPath))
    {
        return PrjFS_Result_ENotAVirtualizationRoot;
    }
    
    s_kernelServiceConnection = PrjFSService_ConnectToDriver(UserClientType_Provider);
    if (IO_OBJECT_NULL == s_kernelServiceConnection)
    {
        return PrjFS_Result_EDriverNotLoaded;
    }
    
    DataQueueResources dataQueue;
    s_messageQueueDispatchQueue = dispatch_queue_create("PrjFS Kernel Message Handling", DISPATCH_QUEUE_SERIAL);
    if (!PrjFSService_DataQueueInit(&dataQueue, s_kernelServiceConnection, ProviderPortType_MessageQueue, ProviderMemoryType_MessageQueue, s_messageQueueDispatchQueue))
    {
        cerr << "Failed to set up shared data queue.\n";
        return PrjFS_Result_EInvalidOperation;
    }
    
    s_virtualizationRootFullPath = virtualizationRootFullPath;
    s_callbacks = callbacks;
    
    errno_t error = RegisterVirtualizationRootPath(virtualizationRootFullPath);
    if (error != 0)
    {
        cerr << "Registering virtualization root failed: " << error << ", " << strerror(error) << endl;
        return PrjFS_Result_EInvalidOperation;
    }
    
    s_kernelRequestHandlingConcurrentQueue = dispatch_queue_create("PrjFS Kernel Request Handling", DISPATCH_QUEUE_CONCURRENT);
    
    dispatch_source_set_event_handler(dataQueue.dispatchSource, ^{
        ClearMachNotification(dataQueue.notificationPort);
        
        while (1)
        {
            IODataQueueEntry* entry = IODataQueuePeek(dataQueue.queueMemory);
            if (nullptr == entry)
            {
                // No more items in queue
                break;
            }
            
            uint32_t messageSize = entry->size;
            if (messageSize < sizeof(Message))
            {
                cerr << "Bad message size: got " << messageSize << " bytes, expected minimum of " << sizeof(Message) << ", skipping. Kernel/user version mismatch?\n";
                IODataQueueDequeue(dataQueue.queueMemory, nullptr, nullptr);
                continue;
            }
            
            void* messageMemory = malloc(messageSize);
            uint32_t dequeuedSize = messageSize;
            IOReturn result = IODataQueueDequeue(dataQueue.queueMemory, messageMemory, &dequeuedSize);
            if (kIOReturnSuccess != result || dequeuedSize != messageSize)
            {
                cerr << "Unexpected result dequeueing message - result 0x" << std::hex << result << " dequeued " << dequeuedSize << "/" << messageSize << " bytes\n";
                abort();
            }
            
            Message message = ParseMessageMemory(messageMemory, messageSize);
            
            // At the moment, we expect all messages to include a path
            assert(message.path != nullptr);

            // Ensure we don't run more than one request handler at once for the same file
            {
                mutex_lock lock(s_PendingRequestMessageMutex);
                typedef unordered_map<string, set<uint64_t>>::iterator PendingMessageIterator;
                    PendingMessageIterator file_messages_found = s_PendingRequestMessageIDs.find(message.path);
                if (file_messages_found == s_PendingRequestMessageIDs.end())
                {
                    // Not handling this file/dir yet
                    std::pair<PendingMessageIterator, bool> inserted =
                        s_PendingRequestMessageIDs.insert(std::make_pair(string(message.path), set<uint64_t>{ message.messageHeader->messageId }));
                    assert(inserted.second);
                }
                else
                {
                    // Already a handler running for this path, don't handle it again.
                    file_messages_found->second.insert(message.messageHeader->messageId);
                    continue;
                }
            }
            

            dispatch_async(
                s_kernelRequestHandlingConcurrentQueue,
                ^{
                    HandleKernelRequest(message, messageMemory);
                });
        }
    });
    dispatch_resume(dataQueue.dispatchSource);
	
    return PrjFS_Result_Success;
}

PrjFS_Result PrjFS_ConvertDirectoryToVirtualizationRoot(
    _In_    const char*                             virtualizationRootFullPath)
{
#ifdef DEBUG
    std::cout << "PrjFS_ConvertDirectoryToVirtualizationRoot(" << virtualizationRootFullPath << ")" << std::endl;
#endif
    
    if (nullptr == virtualizationRootFullPath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    // TODO: walk entire parent chain to root and all child directories to leaf nodes, to make sure we find no other virtualization roots.
    // It is not allowed to have nested virtualization roots.

    if (IsBitSetInFileFlags(virtualizationRootFullPath, FileFlags_IsInVirtualizationRoot) ||
        IsVirtualizationRoot(virtualizationRootFullPath))
    {
        return PrjFS_Result_EVirtualizationRootAlreadyExists;
    }

    PrjFSVirtualizationRootXAttrData rootXattrData = {};
    if (!InitializeEmptyPlaceholder(
            virtualizationRootFullPath,
            &rootXattrData,
            PrjFSVirtualizationRootXAttrName))
    {
        return PrjFS_Result_EIOError;
    }
    
    return PrjFS_Result_Success;
}

PrjFS_Result PrjFS_WritePlaceholderDirectory(
    _In_    const char*                             relativePath)
{
#ifdef DEBUG
    std::cout << "PrjFS_WritePlaceholderDirectory(" << relativePath << ")" << std::endl;
#endif
    
    if (nullptr == relativePath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);

    if (mkdir(fullPath, 0777))
    {
        goto CleanupAndFail;
    }
    
    if (!InitializeEmptyPlaceholder(fullPath))
    {
        goto CleanupAndFail;
    }
    
    return PrjFS_Result_Success;
    
CleanupAndFail:
    // TODO: cleanup the directory on disk if needed
    return PrjFS_Result_EIOError;
}

PrjFS_Result PrjFS_WritePlaceholderFile(
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    unsigned long                           fileSize,
    _In_    uint16_t                                fileMode)
{
#ifdef DEBUG
    std::cout
        << "PrjFS_WritePlaceholderFile("
        << relativePath << ", " 
        << (int)providerId[0] << ", "
        << (int)contentId[0] << ", "
        << fileSize << ")" << std::endl;
#endif
    
    if (nullptr == relativePath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    PrjFSFileXAttrData fileXattrData = {};
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);
    
    // Mode "wbx" means
    //  - Create an empty file if none exists
    //  - Fail if a file already exists at this path
    FILE* file = fopen(fullPath, "wbx");
    if (nullptr == file)
    {
        goto CleanupAndFail;
    }
    
    // Expand the file to the desired size
    if (ftruncate(fileno(file), fileSize))
    {
        goto CleanupAndFail;
    }
    
    fclose(file);
    file = nullptr;
    
    memcpy(fileXattrData.providerId, providerId, PrjFS_PlaceholderIdLength);
    memcpy(fileXattrData.contentId, contentId, PrjFS_PlaceholderIdLength);
    
    if (!InitializeEmptyPlaceholder(
            fullPath,
            &fileXattrData,
            PrjFSFileXAttrName))
    {
        goto CleanupAndFail;
    }
    
    if (chmod(fullPath, fileMode))
    {
        goto CleanupAndFail;
    }

    return PrjFS_Result_Success;
    
CleanupAndFail:
    if (nullptr != file)
    {
        // TODO: we now have a partially created placeholder file. Should we delete it?
        // A better pattern would likely be to create the file in a tmp location, fully initialize its state, then move it into the requested path
        
        fclose(file);
        file = nullptr;
    }
    
    return PrjFS_Result_EIOError;
}

PrjFS_Result PrjFS_WriteFileContents(
    _In_    const PrjFS_FileHandle*                 fileHandle,
    _In_    const void*                             bytes,
    _In_    unsigned int                            byteCount)
{
#ifdef DEBUG
    std::cout
        << "PrjFS_WriteFile("
        << fileHandle->file << ", "
        << (int)((char*)bytes)[0] << ", "
        << (int)((char*)bytes)[1] << ", "
        << (int)((char*)bytes)[2] << ", "
        << byteCount << ")" << std::endl;
#endif
    
    if (nullptr == fileHandle->file ||
        nullptr == bytes)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    if (byteCount != fwrite(bytes, 1, byteCount, fileHandle->file))
    {
        return PrjFS_Result_EIOError;
    }
    
    return PrjFS_Result_Success;
}

// Private functions


static Message ParseMessageMemory(const void* messageMemory, uint32_t size)
{
    const MessageHeader* header = static_cast<const MessageHeader*>(messageMemory);
    if (header->pathSizeBytes + sizeof(*header) != size)
    {
        fprintf(stderr, "ParseMessageMemory: invariant failed, bad message? PathSizeBytes = %u, message size = %u, expecting %zu\n",
            header->pathSizeBytes, size, header->pathSizeBytes + sizeof(*header));
        abort();
    }
            
    const char* path = nullptr;
    if (header->pathSizeBytes > 0)
    {
        path = static_cast<const char*>(messageMemory) + sizeof(*header);
        
        // Path string should fit exactly in reserved memory, with nul terminator in end position
        assert(strnlen(path, header->pathSizeBytes) == header->pathSizeBytes - 1);
    }
    return Message { header, path };
}

static void HandleKernelRequest(Message request, void* messageMemory)
{
    PrjFS_Result result = PrjFS_Result_EIOError;
    
    const MessageHeader* requestHeader = request.messageHeader;
    switch (requestHeader->messageType)
    {
        case MessageType_KtoU_EnumerateDirectory:
        {
            result = HandleEnumerateDirectoryRequest(requestHeader, request.path);
            break;
        }
            
        case MessageType_KtoU_HydrateFile:
        {
            result = HandleHydrateFileRequest(requestHeader, request.path);
            break;
        }
    }
    
    // async callbacks are not yet implemented
    assert(PrjFS_Result_Pending != result);
    
    if (PrjFS_Result_Pending != result)
    {
        MessageType responseType =
            PrjFS_Result_Success == result
            ? MessageType_Response_Success
            : MessageType_Response_Fail;

        std::set<uint64_t> messageIDs;

        {
            mutex_lock lock(s_PendingRequestMessageMutex);
            unordered_map<string, set<uint64_t>>::iterator fileMessageIDsFound = s_PendingRequestMessageIDs.find(request.path);
            assert(fileMessageIDsFound != s_PendingRequestMessageIDs.end());
            messageIDs = std::move(fileMessageIDsFound->second);
            s_PendingRequestMessageIDs.erase(fileMessageIDsFound);
        }

        for (uint64_t messageID : messageIDs)
        {
            SendKernelMessageResponse(messageID, responseType);
        }
    }
    
    free(messageMemory);
}

static PrjFS_Result HandleEnumerateDirectoryRequest(const MessageHeader* request, const char* path)
{
#ifdef DEBUG
    std::cout << "PrjFSLib.HandleKernelRequest: MessageType_KtoU_EnumerateDirectory" << std::endl;
#endif
    
    PrjFS_Result callbackResult = s_callbacks.EnumerateDirectory(0, path, request->pid, request->procname);
    
    if (PrjFS_Result_Success == callbackResult)
    {
        char fullPath[PrjFSMaxPath];
        CombinePaths(s_virtualizationRootFullPath.c_str(), path, fullPath);
        
        if (!SetBitInFileFlags(fullPath, FileFlags_IsEmpty, false))
        {
            // TODO: how should we handle this scenario where the provider thinks it succeeded, but we were unable to
            // update placeholder metadata?
            return PrjFS_Result_EIOError;
        }
    }
    
    return callbackResult;
}

static PrjFS_Result HandleHydrateFileRequest(const MessageHeader* request, const char* path)
{
#ifdef DEBUG
    std::cout << "PrjFSLib.HandleKernelRequest: MessageType_KtoU_HydrateFile" << std::endl;
#endif
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), path, fullPath);
    
    PrjFSFileXAttrData xattrData = {};
    if (!GetXAttr(fullPath, PrjFSFileXAttrName, sizeof(PrjFSFileXAttrData), &xattrData))
    {
        return PrjFS_Result_EIOError;
    }
    
    PrjFS_FileHandle fileHandle;
    
    // Mode "rb+" means:
    //  - The file must already exist
    //  - The handle is opened for reading and writing
    //  - We are allowed to seek to somewhere other than end of stream for writing
    fileHandle.file = fopen(fullPath, "rb+");
    if (nullptr == fileHandle.file)
    {
        return PrjFS_Result_EIOError;
    }
    
    // Seek back to the beginning so the provider can overwrite the empty contents
    if (fseek(fileHandle.file, 0, 0))
    {
        fclose(fileHandle.file);
        return PrjFS_Result_EIOError;
    }
    
    PrjFS_Result callbackResult = s_callbacks.GetFileStream(0, path, xattrData.providerId, xattrData.contentId, request->pid, request->procname, &fileHandle);
    
    // TODO: once we support async callbacks, we'll need to save off the fileHandle if the result is Pending
    
    if (fclose(fileHandle.file))
    {
        // TODO: under what conditions can fclose fail? How do we recover?
        return PrjFS_Result_EIOError;
    }
    
    if (PrjFS_Result_Success == callbackResult)
    {
        // TODO: validate that the total bytes written match the size that was reported on the placeholder in the first place
        // Potential bugs if we don't:
        //  * The provider writes fewer bytes than expected. The hydrated is left with extra padding up to the original reported size.
        //  * The provider writes more bytes than expected. The write succeeds, but whatever tool originally opened the file may have already
        //    allocated the originally reported size, and now the contents appear truncated.
        
        if (!SetBitInFileFlags(fullPath, FileFlags_IsEmpty, false))
        {
            // TODO: how should we handle this scenario where the provider thinks it succeeded, but we were unable to
            // update placeholder metadata?
            return PrjFS_Result_EIOError;
        }
    }
    
    return callbackResult;
}

static bool InitializeEmptyPlaceholder(const char* fullPath)
{
    return
        SetBitInFileFlags(fullPath, FileFlags_IsInVirtualizationRoot, true) &&
        SetBitInFileFlags(fullPath, FileFlags_IsEmpty, true);
}

template<typename TPlaceholder>
static bool InitializeEmptyPlaceholder(const char* fullPath, TPlaceholder* data, const char* xattrName)
{
    if (InitializeEmptyPlaceholder(fullPath))
    {
        data->header.magicNumber = PlaceholderMagicNumber;
        data->header.formatVersion = PlaceholderFormatVersion;
        
        static_assert(std::is_pod<TPlaceholder>(), "TPlaceholder must be a POD struct");
        if (AddXAttr(fullPath, xattrName, data, sizeof(TPlaceholder)))
        {
            return true;
        }
    }
    
    return false;
}

static bool IsVirtualizationRoot(const char* path)
{
    PrjFSVirtualizationRootXAttrData data = {};
    if (GetXAttr(path, PrjFSVirtualizationRootXAttrName, sizeof(PrjFSVirtualizationRootXAttrData), &data))
    {
        return true;
    }

    return false;
}

static void CombinePaths(const char* root, const char* relative, char (&combined)[PrjFSMaxPath])
{
    snprintf(combined, PrjFSMaxPath, "%s/%s", root, relative);
}

static bool SetBitInFileFlags(const char* path, uint32_t bit, bool value)
{
    struct stat fileAttributes;
    if (stat(path, &fileAttributes))
    {
        return false;
    }
    
    uint32_t newValue;
    if (value)
    {
        newValue = fileAttributes.st_flags | bit;
    }
    else
    {
        newValue = fileAttributes.st_flags & ~bit;
    }
    
    if (chflags(path, newValue))
    {
        return false;
    }
    
    return true;
}

static bool IsBitSetInFileFlags(const char* path, uint32_t bit)
{
    struct stat fileAttributes;
    if (stat(path, &fileAttributes))
    {
        return false;
    }

    return fileAttributes.st_flags & bit;
}

static bool AddXAttr(const char* path, const char* name, const void* value, size_t size)
{
    if (setxattr(path, name, value, size, 0, 0))
    {
        return false;
    }
    
    return true;
}

static bool GetXAttr(const char* path, const char* name, size_t size, _Out_ void* value)
{
    if (getxattr(path, name, value, size, 0, 0) == size)
    {
        // TODO: also validate the magic number and format version.
        // It's easy to check their expected values, but we will need to decide what to do if they are incorrect.

        return true;
    }
    
    return false;
}

static errno_t SendKernelMessageResponse(uint64_t messageId, MessageType responseType)
{
    const uint64_t inputs[] = { messageId, responseType };
    IOReturn callResult = IOConnectCallScalarMethod(
        s_kernelServiceConnection,
        ProviderSelector_KernelMessageResponse,
        inputs, std::extent<decltype(inputs)>::value, // scalar inputs
        nullptr, nullptr);                            // no outputs
    return callResult == kIOReturnSuccess ? 0 : EBADMSG;
}

static errno_t RegisterVirtualizationRootPath(const char* path)
{
    uint64_t error = EBADMSG;
    uint32_t output_count = 1;
    size_t pathSize = strlen(path) + 1;
    IOReturn callResult = IOConnectCallMethod(
        s_kernelServiceConnection,
        ProviderSelector_RegisterVirtualizationRootPath,
        nullptr, 0, // no scalar inputs
        path, pathSize, // struct input
        &error, &output_count, // scalar output
        nullptr, nullptr); // no struct output
    assert(callResult == kIOReturnSuccess);
    return static_cast<errno_t>(error);
}

static void ClearMachNotification(mach_port_t port)
{
    struct {
        mach_msg_header_t	msgHdr;
        mach_msg_trailer_t	trailer;
    } msg;
    mach_msg(&msg.msgHdr, MACH_RCV_MSG | MACH_RCV_TIMEOUT, 0, sizeof(msg), port, 0, MACH_PORT_NULL);
}
