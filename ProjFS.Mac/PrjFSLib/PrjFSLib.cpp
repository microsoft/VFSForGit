#include <iostream>
#include <cassert>
#include <stddef.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/sys_domain.h>
#include <sys/xattr.h>
#include <sys/fsgetpath.h>
#include <thread>
#include <unistd.h>
#include <dirent.h>
#include <queue>
#include <stack>
#include <memory>
#include <set>
#include <map>
#include <IOKit/IOKitLib.h>
#include <IOKit/IODataQueueClient.h>
#include <mach/mach_port.h>
#include <CoreFoundation/CFNumber.h>
#include <string>
#include <sstream>

#include "stdlib.h"

#include "PrjFSLib.h"
#include "../PrjFSKext/public/PrjFSCommon.h"
#include "../PrjFSKext/public/PrjFSXattrs.h"
#include "../PrjFSKext/public/Message.h"
#include "PrjFSUser.hpp"

#define STRINGIFY(s) #s

using std::cerr;
using std::cout;
using std::dec;
using std::endl;
using std::extent;
using std::hex;
using std::is_pod;
using std::lock_guard;
using std::make_pair;
using std::make_shared;
using std::map;
using std::move;
using std::mutex;
using std::oct;
using std::ostringstream;
using std::pair;
using std::queue;
using std::set;
using std::shared_ptr;
using std::stack;
using std::string;

typedef lock_guard<mutex> mutex_lock;

// Structs
struct _PrjFS_FileHandle
{
    FILE* file;
};

struct FsidInodeCompare
{
    bool operator() (const FsidInode& lhs, const FsidInode& rhs) const
    {
        if (lhs.fsid.val[0] !=  rhs.fsid.val[0])
        {
            return lhs.fsid.val[0] < rhs.fsid.val[0];
        }
        
        if (lhs.fsid.val[1] !=  rhs.fsid.val[1])
        {
            return lhs.fsid.val[1] < rhs.fsid.val[1];
        }
        
        return lhs.inode < rhs.inode;
    }
};

struct MutexAndUseCount
{
    shared_ptr<mutex> mutex;
    int useCount;
};

typedef map<FsidInode, MutexAndUseCount, FsidInodeCompare> FileMutexMap;

// Function prototypes
static bool SetBitInFileFlags(const char* fullPath, uint32_t bit, bool value);
static bool IsBitSetInFileFlags(const char* fullPath, uint32_t bit);

static bool InitializeEmptyPlaceholder(const char* fullPath);
template<typename TPlaceholder> static bool InitializeEmptyPlaceholder(const char* fullPath, TPlaceholder* data, const char* xattrName);
static errno_t AddXAttr(const char* fullPath, const char* name, const void* value, size_t size);
static bool TryGetXAttr(const char* fullPath, const char* name, size_t expectedSize, _Out_ void* value);
static errno_t RemoveXAttrWithoutFollowingLinks(const char* fullPath, const char* name);

static inline PrjFS_NotificationType KUMessageTypeToNotificationType(MessageType kuNotificationType);

static bool IsVirtualizationRoot(const char* fullPath);
static void CombinePaths(const char* root, const char* relative, char (&combined)[PrjFSMaxPath]);
static const char* GetRelativePath(const char* fullPath, const char* root);

static errno_t SendKernelMessageResponse(uint64_t messageId, MessageType responseType);
static errno_t RegisterVirtualizationRootPath(const char* fullPath);

static PrjFS_Result RecursivelyMarkAllChildrenAsInRoot(const char* fullDirectoryPath);

static void HandleKernelRequest(void* messageMemory, uint32_t messageSize);
static PrjFS_Result HandleEnumerateDirectoryRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath);
static PrjFS_Result HandleRecursivelyEnumerateDirectoryRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath);
static PrjFS_Result HandleHydrateFileRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath);
static PrjFS_Result HandleNewFileInRootNotification(
    const MessageHeader* request,
    const char* relativePath,
    const char* fullPath,
    bool isDirectory,
    PrjFS_NotificationType notificationType);
static PrjFS_Result HandleFileNotification(
    const MessageHeader* request,
    const char* relativePath,
    const char* fullPath,
    bool isDirectory,
    PrjFS_NotificationType notificationType);

static void FindNewFoldersInRootAndNotifyProvider(const MessageHeader* request, const char* relativePath);
static bool IsDirEntChildDirectory(const dirent* directoryEntry);

static Message ParseMessageMemory(const void* messageMemory, uint32_t size);

#ifdef DEBUG
static const char* NotificationTypeToString(PrjFS_NotificationType notificationType);
#endif

static FileMutexMap::iterator CheckoutFileMutexIterator(const FsidInode& fsidInode);
static void ReturnFileMutexIterator(FileMutexMap::iterator lockIterator);

// State
static io_connect_t s_kernelServiceConnection = IO_OBJECT_NULL;
static string s_virtualizationRootFullPath;
static PrjFS_Callbacks s_callbacks;
static dispatch_queue_t s_messageQueueDispatchQueue;
static dispatch_queue_t s_kernelRequestHandlingConcurrentQueue;

// Map of FsidInode -> MutexAndUseCount for that FsidInode, plus mutex to protect the map itself.
FileMutexMap s_fileLocks;
mutex s_fileLocksMutex;

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
    cout
        << "PrjFS_StartVirtualizationInstance("
        << virtualizationRootFullPath << ", "
        << callbacks.EnumerateDirectory << ", "
        << callbacks.GetFileStream << ", "
        << callbacks.NotifyOperation << ", "
        << callbacks.LogError << ","
        << poolThreadCount << ")" << endl;
#endif
    
    if (nullptr == virtualizationRootFullPath ||
        nullptr == callbacks.EnumerateDirectory ||
        nullptr == callbacks.GetFileStream ||
        nullptr == callbacks.NotifyOperation ||
        nullptr == callbacks.LogError)
    {
        return PrjFS_Result_EInvalidArgs;
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
        DataQueue_ClearMachNotification(dataQueue.notificationPort);
        
        while (1)
        {
            IODataQueueEntry* entry = DataQueue_Peek(dataQueue.queueMemory);
            if (nullptr == entry)
            {
                // No more items in queue
                break;
            }
            
            uint32_t messageSize = entry->size;
            if (messageSize < sizeof(Message))
            {
                cerr << "Bad message size: got " << messageSize << " bytes, expected minimum of " << sizeof(Message) << ", skipping. Kernel/user version mismatch?\n";
                DataQueue_Dequeue(dataQueue.queueMemory, nullptr, nullptr);
                continue;
            }
            
            void* messageMemory = malloc(messageSize);
            uint32_t dequeuedSize = messageSize;
            IOReturn result = DataQueue_Dequeue(dataQueue.queueMemory, messageMemory, &dequeuedSize);
            if (kIOReturnSuccess != result || dequeuedSize != messageSize)
            {
                cerr << "Unexpected result dequeueing message - result 0x" << hex << result << " dequeued " << dequeuedSize << "/" << messageSize << " bytes\n";
                abort();
            }

            dispatch_async(
                s_kernelRequestHandlingConcurrentQueue,
                ^{
                    HandleKernelRequest(messageMemory, messageSize);
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
    cout
        << "PrjFS_ConvertDirectoryToVirtualizationRoot("
        << virtualizationRootFullPath
        << ")" << endl;
#endif
    
    if (nullptr == virtualizationRootFullPath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    // TODO(Mac): walk entire parent chain to root and all child directories to leaf nodes, to make sure we find no other virtualization roots.
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
    
    return RecursivelyMarkAllChildrenAsInRoot(virtualizationRootFullPath);
}

PrjFS_Result PrjFS_WritePlaceholderDirectory(
    _In_    const char*                             relativePath)
{
#ifdef DEBUG
    cout
        << "PrjFS_WritePlaceholderDirectory("
        << relativePath
        << ")" << endl;
#endif
    
    if (nullptr == relativePath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    PrjFS_Result result = PrjFS_Result_Invalid;
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);

    if (mkdir(fullPath, 0777))
    {
        switch(errno)
        {
            // TODO(Mac): Return more specific error codes for other failure scenarios
            case ENOENT: // A component of the path prefix does not exist or path is an empty string
                result = PrjFS_Result_EPathNotFound;
                break;
            default:
                result = PrjFS_Result_EIOError;
                break;
        }
        
        goto CleanupAndFail;
    }
    
    if (!InitializeEmptyPlaceholder(fullPath))
    {
        result = PrjFS_Result_EIOError;
        goto CleanupAndFail;
    }
    
    return PrjFS_Result_Success;
    
CleanupAndFail:
    // TODO(Mac): cleanup the directory on disk if needed
    return result;
}

PrjFS_Result PrjFS_WritePlaceholderFile(
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    unsigned long                           fileSize,
    _In_    uint16_t                                fileMode)
{
#ifdef DEBUG
    cout
        << "PrjFS_WritePlaceholderFile("
        << relativePath << ", " 
        << (int)providerId[0] << ", "
        << (int)contentId[0] << ", "
        << fileSize << ", "
        << oct << fileMode << dec << ")" << endl;
#endif
    
    if (nullptr == relativePath)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    PrjFS_Result result = PrjFS_Result_Invalid;
    PrjFSFileXAttrData fileXattrData = {};
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);
    
    // Mode "wx" means:
    //  - "w": Open for writing.  The stream is positioned at the beginning of the file.  Create the file if it does not exist.
    //  - "x": If the file already exists, fopen() fails, and sets errno to EEXIST.
    FILE* file = fopen(fullPath, "wx");
    if (nullptr == file)
    {
        switch(errno)
        {
            // TODO(Mac): Return more specific error codes for other failure scenarios
            case ENOENT: // A directory component in fullPath does not exist or is a dangling symbolic link.
                result = PrjFS_Result_EPathNotFound;
                break;
            case EEXIST: // The file already exists
            default:
                result = PrjFS_Result_EIOError;
                break;
        }
        
        goto CleanupAndFail;
    }
    
    // Expand the file to the desired size
    if (ftruncate(fileno(file), fileSize))
    {
        result = PrjFS_Result_EIOError;
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
        result = PrjFS_Result_EIOError;
        goto CleanupAndFail;
    }
    
    // TODO(Mac): Only call chmod if fileMode is different than the default file mode
    if (chmod(fullPath, fileMode))
    {
        result = PrjFS_Result_EIOError;
        goto CleanupAndFail;
    }

    return PrjFS_Result_Success;
    
CleanupAndFail:
    if (nullptr != file)
    {
        // TODO(Mac) #234: we now have a partially created placeholder file. Should we delete it?
        // A better pattern would likely be to create the file in a tmp location, fully initialize its state, then move it into the requested path
        
        fclose(file);
        file = nullptr;
    }
    
    return result;
}

PrjFS_Result PrjFS_WriteSymLink(
    _In_    const char*                             relativePath,
    _In_    const char*                             symLinkTarget)
{
#ifdef DEBUG
    cout
        << "PrjFS_WriteSymLink("
        << relativePath << ", "
        << symLinkTarget << ")" << endl;
#endif
    
    if (nullptr == relativePath || nullptr == symLinkTarget)
    {
        return PrjFS_Result_EInvalidArgs;
    }
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);
    
    if(symlink(symLinkTarget, fullPath))
    {
        goto CleanupAndFail;
    }
    
    // TODO(Mac) #391: Handles failures of SetBitInFileFlags
    SetBitInFileFlags(fullPath, FileFlags_IsInVirtualizationRoot, true);

    return PrjFS_Result_Success;
    
CleanupAndFail:
    
    return PrjFS_Result_EIOError;
    
}

PrjFS_Result PrjFS_UpdatePlaceholderFileIfNeeded(
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    unsigned long                           fileSize,
    _In_    uint16_t                                fileMode,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause)
{
#ifdef DEBUG
    cout
        << "PrjFS_UpdatePlaceholderFileIfNeeded("
        << relativePath << ", "
        << (int)providerId[0] << ", "
        << (int)contentId[0] << ", "
        << fileSize << ", "
        << oct << fileMode << dec << ", "
        << hex << updateFlags << dec << ")" << endl;
#endif
    
    // TODO(Mac): Check if the contentId or fileMode have changed before proceeding
    // with the update
    
    PrjFS_Result result = PrjFS_DeleteFile(relativePath, updateFlags, failureCause);
    if (result != PrjFS_Result_Success)
    {
       return result;
    }

    // TODO(Mac): Ensure that races with hydration are handled properly
    return PrjFS_WritePlaceholderFile(relativePath, providerId, contentId, fileSize, fileMode);
}

PrjFS_Result PrjFS_ReplacePlaceholderFileWithSymLink(
    _In_    const char*                             relativePath,
    _In_    const char*                             symLinkTarget,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause)
{
#ifdef DEBUG
    cout
        << "PrjFS_ReplacePlaceholderFileWithSymLink("
        << relativePath << ", "
        << symLinkTarget << ", "
        << hex << updateFlags << dec << ")" << endl;
#endif
    
    PrjFS_Result result = PrjFS_DeleteFile(relativePath, updateFlags, failureCause);
    if (result != PrjFS_Result_Success)
    {
       return result;
    }
    
    return PrjFS_WriteSymLink(relativePath, symLinkTarget);
}

PrjFS_Result PrjFS_DeleteFile(
    _In_    const char*                             relativePath,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause)
{
#ifdef DEBUG
    cout
        << "PrjFS_DeleteFile("
        << relativePath << ", "
        << hex << updateFlags << dec << ")" << endl;
#endif
    
    *failureCause = PrjFS_UpdateFailureCause_Invalid;
    
    if (nullptr == relativePath)
    {
        return PrjFS_Result_EInvalidArgs;
    }

    // TODO(Mac): Ensure that races with hydration are handled properly
    
    char fullPath[PrjFSMaxPath];
    CombinePaths(s_virtualizationRootFullPath.c_str(), relativePath, fullPath);
    
    struct stat path_stat;
    if (0 != stat(fullPath, &path_stat))
    {
        switch(errno)
        {
            case ENOENT:  // A component of fullPath does not exist
            case ENOTDIR: // A component of fullPath is not a directory
                return PrjFS_Result_Success;
            default:
                return PrjFS_Result_EIOError;
        }
    }
    
    if (!(S_ISREG(path_stat.st_mode) || S_ISDIR(path_stat.st_mode)))
    {
        // Only files and directories can be deleted with PrjFS_DeleteFile
        // Anything else should be treated as a full file
        *failureCause = PrjFS_UpdateFailureCause_FullFile;
        return PrjFS_Result_EVirtualizationInvalidOperation;
    }
    
    if (S_ISREG(path_stat.st_mode))
    {
        // TODO(Mac): Determine if we need a similar check for directories as well
        PrjFSFileXAttrData xattrData = {};
        if (!TryGetXAttr(fullPath, PrjFSFileXAttrName, sizeof(PrjFSFileXAttrData), &xattrData))
        {
            *failureCause = PrjFS_UpdateFailureCause_FullFile;
            return PrjFS_Result_EVirtualizationInvalidOperation;
        }
    }
    
    if (0 != remove(fullPath))
    {
        switch(errno)
        {
            case ENOENT:  // A component of fullPath does not exist
            case ENOTDIR: // A component of fullPath is not a directory
                return PrjFS_Result_Success;
            case ENOTEMPTY:
                return PrjFS_Result_EDirectoryNotEmpty;
            default:
                return PrjFS_Result_EIOError;
        }
    }

    return PrjFS_Result_Success;
}

PrjFS_Result PrjFS_WriteFileContents(
    _In_    const PrjFS_FileHandle*                 fileHandle,
    _In_    const void*                             bytes,
    _In_    unsigned int                            byteCount)
{
#ifdef DEBUG
    cout
        << "PrjFS_WriteFile("
        << fileHandle->file << ", "
        << (int)((char*)bytes)[0] << ", "
        << (int)((char*)bytes)[1] << ", "
        << (int)((char*)bytes)[2] << ", "
        << byteCount << ")" << endl;
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


static void ParseMessageString(uint16_t stringSize,  uint32_t& messageBytesRemain, const char*& messagePosition, const char*& outParsedString)
{
    if (stringSize > 0)
    {
        assert(messageBytesRemain >= stringSize);
        const char* string = messagePosition;
        // Path string should fit exactly in reserved memory, with nul terminator in end position
        assert(strnlen(string, stringSize) == stringSize - 1);
        messagePosition += stringSize;
        messageBytesRemain -= stringSize;
        
        outParsedString = string;
    }
}

static Message ParseMessageMemory(const void* messageMemory, uint32_t size)
{
    const MessageHeader* header = static_cast<const MessageHeader*>(messageMemory);
    if (size != sizeof(*header) + header->pathSizeBytes + header->fromPathSizeBytes)
    {
        fprintf(stderr, "ParseMessageMemory: invariant failed, bad message? message size = %u, expecting minimum of %zu\n",
            size, sizeof(*header));
        abort();
    }
    
    Message parsedMessage = { header };
    
    const char* messagePosition = static_cast<const char*>(messageMemory) + sizeof(*header);
    uint32_t messageBytesRemain = size - sizeof(*header);

    ParseMessageString(header->pathSizeBytes, messageBytesRemain, messagePosition, parsedMessage.path);
    ParseMessageString(header->fromPathSizeBytes, messageBytesRemain, messagePosition, parsedMessage.fromPath);

    assert(messageBytesRemain == 0);

    return parsedMessage;
}

static void HandleKernelRequest(void* messageMemory, uint32_t messageSize)
{
    PrjFS_Result result = PrjFS_Result_EIOError;
    
    Message request = ParseMessageMemory(messageMemory, messageSize);
    const MessageHeader* requestHeader = request.messageHeader;
    
    const char* absolutePath = nullptr;
    const char* relativePath = nullptr;
    
    // We expect a non-null request.path for messages sent from the FILEOP handler,
    // whereas messages originating in the kext's vnode handler will only fill
    // the fsid/inode, so we need to look up the path below.
    char pathBuffer[PrjFSMaxPath];
    if (request.path == nullptr)
    {
        fsid_t fsid = request.messageHeader->fsidInode.fsid;
        ssize_t pathSize = fsgetpath(pathBuffer, sizeof(pathBuffer), &fsid, request.messageHeader->fsidInode.inode);

        if (pathSize < 0)
        {
            // TODO(Mac): Add this message to PrjFSLib logging once available (#395)
            ostringstream ss;
            ss
                << "MessageType: " << requestHeader->messageType << " "
                << "PrjFSLib.HandleKernelRequest: fsgetpath failed for fsid 0x"
                << hex << fsid.val[0] << ":" << hex << fsid.val[1]
                << ", inode "
                << dec << request.messageHeader->fsidInode.inode
                << "; error = "
                << errno
                << "(" << strerror(errno) << ")"
                << endl;
            string errorMessage = ss.str();

            s_callbacks.LogError(errorMessage.c_str());
            result = PrjFS_Result_Success;
            goto CleanupAndReturn;
        }
        else
        {
            absolutePath = pathBuffer;
            relativePath = GetRelativePath(pathBuffer, s_virtualizationRootFullPath.c_str());
#if DEBUG
            cout
                << "PrjFSLib.HandleKernelRequest: fsgetpath for fsid 0x"
                << hex << fsid.val[0] << ":" << hex << fsid.val[1]
                << ", inode "
                << dec << request.messageHeader->fsidInode.inode
                << " -> '"
                << pathBuffer
                << "' -> relative path '"
                << (request.path != nullptr ? request.path : "[NULL]")
                << "'"
                << endl;
#endif
        }
    }
    else
    {
        absolutePath = request.path;
        relativePath = GetRelativePath(absolutePath, s_virtualizationRootFullPath.c_str());
    }
    
    switch (requestHeader->messageType)
    {
        case MessageType_KtoU_EnumerateDirectory:
        {
            result = HandleEnumerateDirectoryRequest(requestHeader, absolutePath, relativePath);
            break;
        }
        
        case MessageType_KtoU_RecursivelyEnumerateDirectory:
        {
            result = HandleRecursivelyEnumerateDirectoryRequest(requestHeader, absolutePath, relativePath);
            break;
        }
            
        case MessageType_KtoU_HydrateFile:
        {
            result = HandleHydrateFileRequest(requestHeader, absolutePath, relativePath);
            break;
        }
            
        case MessageType_KtoU_NotifyFileModified:
        case MessageType_KtoU_NotifyFilePreDelete:
        case MessageType_KtoU_NotifyDirectoryPreDelete:
        case MessageType_KtoU_NotifyFilePreConvertToFull:
        {
            result = HandleFileNotification(
                requestHeader,
                relativePath,
                absolutePath,
                requestHeader->messageType == MessageType_KtoU_NotifyDirectoryPreDelete,  // isDirectory
                KUMessageTypeToNotificationType(static_cast<MessageType>(requestHeader->messageType)));
            break;
        }
        
        case MessageType_KtoU_NotifyFileRenamed:
        case MessageType_KtoU_NotifyDirectoryRenamed:
        case MessageType_KtoU_NotifyFileHardLinkCreated:
        {
#if DEBUG
            // TODO(Mac): Move the following line out of the DEBUG block once we actually need the information. Currently just causes warning-as-error in release build.
            const char* relativeFromPath = GetRelativePath(request.fromPath, s_virtualizationRootFullPath.c_str());

            cout << "PrjFSLib.HandleKernelRequest: " << (requestHeader->messageType == MessageType_KtoU_NotifyFileHardLinkCreated ? "hard-linked " : "renamed ") << request.fromPath << " -> " << absolutePath << " (absolute), ";
            if (relativeFromPath != nullptr)
            {
                cout << "from this root (relative path " << relativeFromPath << ") ";
            }
            if (relativePath != nullptr)
            {
                cout << "into this root (relative path " << relativePath << ")";
            }
            cout << endl;
#endif
            
            bool isDirectory = requestHeader->messageType == MessageType_KtoU_NotifyDirectoryRenamed;
            
            if (relativePath != nullptr)
            {
                result = HandleNewFileInRootNotification(
                    requestHeader,
                    relativePath,
                    absolutePath,
                    isDirectory,
                    KUMessageTypeToNotificationType(static_cast<MessageType>(requestHeader->messageType)));
            }
            
            break;
        }

        case MessageType_KtoU_NotifyFileCreated:
        {
            result = HandleNewFileInRootNotification(
                requestHeader,
                relativePath,
                absolutePath,
                false, // not a directory
                KUMessageTypeToNotificationType(static_cast<MessageType>(requestHeader->messageType)));
            break;
        }
    }
    
    // async callbacks are not yet implemented
    assert(PrjFS_Result_Pending != result);
    
CleanupAndReturn:
    if (PrjFS_Result_Pending != result)
    {
        MessageType responseType =
            PrjFS_Result_Success == result
            ? MessageType_Response_Success
            : MessageType_Response_Fail;
        
            SendKernelMessageResponse(requestHeader->messageId, responseType);
    }
    
    free(messageMemory);
}

static PrjFS_Result HandleEnumerateDirectoryRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath)
{
#ifdef DEBUG
    cout
        << "PrjFSLib.HandleEnumerateDirectoryRequest: "
        << absolutePath
        << " (root-relative: " << relativePath << ")"
        << " Process name: " << request->procname
        << " Pid: " << request->pid
        << endl;
#endif
    
    if (!IsBitSetInFileFlags(absolutePath, FileFlags_IsEmpty))
    {
        return PrjFS_Result_Success;
    }
    
    PrjFS_Result result;
    FileMutexMap::iterator mutexIterator = CheckoutFileMutexIterator(request->fsidInode);
    {
        mutex_lock lock(*(mutexIterator->second.mutex));
        if (!IsBitSetInFileFlags(absolutePath, FileFlags_IsEmpty))
        {
            result = PrjFS_Result_Success;
            goto CleanupAndReturn;
        }
    
        result = s_callbacks.EnumerateDirectory(
            0 /* commandId */,
            relativePath,
            request->pid,
            request->procname);
        
        if (PrjFS_Result_Success == result)
        {
            if (!SetBitInFileFlags(absolutePath, FileFlags_IsEmpty, false))
            {
                // TODO(Mac): how should we handle this scenario where the provider thinks it succeeded, but we were unable to
                // update placeholder metadata?
                result = PrjFS_Result_EIOError;
            }
        }
    }

CleanupAndReturn:
    ReturnFileMutexIterator(mutexIterator);

    return result;
}

static PrjFS_Result HandleRecursivelyEnumerateDirectoryRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath)
{
#ifdef DEBUG
    cout
        << "PrjFSLib.HandleRecursivelyEnumerateDirectoryRequest: "
        << absolutePath
        << " (root-relative: " << relativePath << ")"
        << " Process name: " << request->procname
        << " Pid: " << request->pid
        << endl;
#endif
    
    DIR* directory = nullptr;
    PrjFS_Result result = PrjFS_Result_Success;
    queue<string> directoryRelativePaths;
    directoryRelativePaths.push(relativePath);
    
    // Walk each directory, expanding those that are found to be empty
    char path[PrjFSMaxPath];
    while (!directoryRelativePaths.empty())
    {
        string directoryRelativePath(directoryRelativePaths.front());
        directoryRelativePaths.pop();
        
        CombinePaths(s_virtualizationRootFullPath.c_str(), directoryRelativePath.c_str(), path);
    
        PrjFS_Result result = HandleEnumerateDirectoryRequest(request, path, directoryRelativePath.c_str());
        if (result != PrjFS_Result_Success)
        {
            goto CleanupAndReturn;
        }
        
        DIR* directory = opendir(path);
        if (nullptr == directory)
        {
            result = PrjFS_Result_EIOError;
            goto CleanupAndReturn;
        }
        
        dirent* dirEntry = readdir(directory);
        while (dirEntry != nullptr)
        {
            if (IsDirEntChildDirectory(dirEntry))
            {
                CombinePaths(directoryRelativePath.c_str(), dirEntry->d_name, path);
                directoryRelativePaths.emplace(path);
            }
            
            dirEntry = readdir(directory);
        }
        
        closedir(directory);
    }
    
CleanupAndReturn:
    if (directory != nullptr)
    {
        closedir(directory);
    }
    
    return result;
}

static PrjFS_Result HandleHydrateFileRequest(const MessageHeader* request, const char* absolutePath, const char* relativePath)
{
#ifdef DEBUG
    cout
        << "PrjFSLib.HandleHydrateFileRequest: "
        << absolutePath
        << " (root-relative: " << relativePath << ")"
        << " Process name: " << request->procname
        << " Pid: " << request->pid
        << endl;
#endif
        
    PrjFSFileXAttrData xattrData = {};
    if (!TryGetXAttr(absolutePath, PrjFSFileXAttrName, sizeof(PrjFSFileXAttrData), &xattrData))
    {
        return PrjFS_Result_EIOError;
    }
    
    if (!IsBitSetInFileFlags(absolutePath, FileFlags_IsEmpty))
    {
        return PrjFS_Result_Success;
    }
    
    PrjFS_Result result;
    PrjFS_FileHandle fileHandle;
    
    FileMutexMap::iterator mutexIterator = CheckoutFileMutexIterator(request->fsidInode);
    
    {
        mutex_lock lock(*(mutexIterator->second.mutex));
        if (!IsBitSetInFileFlags(absolutePath, FileFlags_IsEmpty))
        {
            result = PrjFS_Result_Success;
            goto CleanupAndReturn;
        }
        
        // Mode "rb+" means:
        //  - The file must already exist
        //  - The handle is opened for reading and writing
        //  - We are allowed to seek to somewhere other than end of stream for writing
        fileHandle.file = fopen(absolutePath, "rb+");
        if (nullptr == fileHandle.file)
        {
            result = PrjFS_Result_EIOError;
            goto CleanupAndReturn;
        }
        
        // Seek back to the beginning so the provider can overwrite the empty contents
        if (fseek(fileHandle.file, 0, 0))
        {
            fclose(fileHandle.file);
            result = PrjFS_Result_EIOError;
            goto CleanupAndReturn;
        }
        
        result = s_callbacks.GetFileStream(
            0 /* comandId */,
            relativePath,
            xattrData.providerId,
            xattrData.contentId,
            request->pid,
            request->procname,
            &fileHandle);
        
        // TODO(Mac): once we support async callbacks, we'll need to save off the fileHandle if the result is Pending
        
        fflush(fileHandle.file);
        
        // Don't block on closing the file to avoid deadlock with some Antivirus software
        dispatch_async(s_kernelRequestHandlingConcurrentQueue, ^{
            if (fclose(fileHandle.file))
            {
                // TODO(Mac): under what conditions can fclose fail? How do we recover?
            }
        });
        
        if (PrjFS_Result_Success == result)
        {
            // TODO(Mac): validate that the total bytes written match the size that was reported on the placeholder in the first place
            // Potential bugs if we don't:
            //  * The provider writes fewer bytes than expected. The hydrated is left with extra padding up to the original reported size.
            //  * The provider writes more bytes than expected. The write succeeds, but whatever tool originally opened the file may have already
            //    allocated the originally reported size, and now the contents appear truncated.
            
            if (!SetBitInFileFlags(absolutePath, FileFlags_IsEmpty, false))
            {
                // TODO(Mac): how should we handle this scenario where the provider thinks it succeeded, but we were unable to
                // update placeholder metadata?
                result = PrjFS_Result_EIOError;
            }
        }
    }

CleanupAndReturn:
    ReturnFileMutexIterator(mutexIterator);
    return result;
}

static PrjFS_Result HandleNewFileInRootNotification(
    const MessageHeader* request,
    const char* relativePath,
    const char* absolutePath,
    bool isDirectory,
    PrjFS_NotificationType notificationType)
{
#ifdef DEBUG
    cout
        << "HandleNewFileInRootNotification: "
        << absolutePath
        << " (root-relative: " << relativePath << ")"
        << " Process name: " << request->procname
        << " Pid: " << request->pid
        << " notificationType: " << NotificationTypeToString(notificationType)
        << " isDirectory: " << isDirectory << endl;
#endif

    // Whenever a new file shows up in the root, we need to check if its ancestor
    // directories are flagged as in root.  If they are not, flag them as in root and
    // notify the provider
    FindNewFoldersInRootAndNotifyProvider(request, relativePath);
    
    PrjFS_Result result = HandleFileNotification(
        request,
        relativePath,
        absolutePath,
        isDirectory,
        notificationType);
    
    // TODO(Mac) #391: Handle SetBitInFileFlags failures
    SetBitInFileFlags(absolutePath, FileFlags_IsInVirtualizationRoot, true);
    
    return result;
}

static PrjFS_Result HandleFileNotification(
    const MessageHeader* request,
    const char* relativePath,
    const char* absolutePath,
    bool isDirectory,
    PrjFS_NotificationType notificationType)
{
#ifdef DEBUG
    cout
        << "PrjFSLib.HandleFileNotification: "
        << absolutePath
        << " (root-relative: " << relativePath << ")"
        << " Process name: " << request->procname
        << " Pid: " << request->pid
        << " notificationType: " << NotificationTypeToString(notificationType)
        << " isDirectory: " << isDirectory << endl;
#endif
    
    PrjFSFileXAttrData xattrData = {};
    bool placeholderFile = TryGetXAttr(absolutePath, PrjFSFileXAttrName, sizeof(PrjFSFileXAttrData), &xattrData);

    PrjFS_Result result = s_callbacks.NotifyOperation(
        0 /* commandId */,
        relativePath,
        xattrData.providerId,
        xattrData.contentId,
        request->pid,
        request->procname,
        isDirectory,
        notificationType,
        nullptr /* destinationRelativePath */);
    
    if (result == 0 && placeholderFile && PrjFS_NotificationType_PreConvertToFull == notificationType)
    {
        errno_t result = RemoveXAttrWithoutFollowingLinks(absolutePath, PrjFSFileXAttrName);
        if (0 != result)
        {
            // TODO(Mac) #395: Log error
            // Note that it's expected that RemoveXAttrWithoutFollowingLinks return ENOATTR if
            // another thread has removed the attribute
        }
    }
    
    return result;
}

static void FindNewFoldersInRootAndNotifyProvider(const MessageHeader* request, const char* relativePath)
{
    // Walk up the directory tree and notify the provider about any directories
    // not flagged as being in the root
    stack<pair<string /*relative path*/, string /*full path*/>> newFolderPaths;
    string parentPath(relativePath);
    size_t lastDirSeparator = parentPath.find_last_of('/');
    while (lastDirSeparator != string::npos && lastDirSeparator > 0)
    {
        parentPath = parentPath.substr(0, lastDirSeparator);
        char parentFullPath[PrjFSMaxPath];
        CombinePaths(s_virtualizationRootFullPath.c_str(), parentPath.c_str(), parentFullPath);
        if (IsBitSetInFileFlags(parentFullPath, FileFlags_IsInVirtualizationRoot))
        {
            break;
        }
        else
        {
            newFolderPaths.emplace(make_pair(parentPath, parentFullPath));
            lastDirSeparator = parentPath.find_last_of('/');
        }
    }

    while (!newFolderPaths.empty())
    {
        const pair<string /*relative path*/, string /*full path*/>& parentFolderPath = newFolderPaths.top();

        HandleFileNotification(
            request,
            parentFolderPath.first.c_str(),
            parentFolderPath.second.c_str(),
            true, // isDirectory
            PrjFS_NotificationType_NewFileCreated);
        
        // TODO(Mac) #391: Handle SetBitInFileFlags failures
        SetBitInFileFlags(parentFolderPath.second.c_str(), FileFlags_IsInVirtualizationRoot, true);
        
        newFolderPaths.pop();
    }
}

static bool IsDirEntChildDirectory(const dirent* directoryEntry)
{
    return
        directoryEntry->d_type == DT_DIR &&
        0 != strncmp(".", directoryEntry->d_name, sizeof(directoryEntry->d_name)) &&
        0 != strncmp("..", directoryEntry->d_name, sizeof(directoryEntry->d_name));
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
        
        static_assert(is_pod<TPlaceholder>(), "TPlaceholder must be a POD struct");
        
        errno_t result = AddXAttr(fullPath, xattrName, data, sizeof(TPlaceholder));
        if (0 == result)
        {
            return true;
        }
        else
        {
            // TODO(Mac) #395: Log result
        }
    }
    
    return false;
}

static bool IsVirtualizationRoot(const char* fullPath)
{
    PrjFSVirtualizationRootXAttrData data = {};
    if (TryGetXAttr(fullPath, PrjFSVirtualizationRootXAttrName, sizeof(PrjFSVirtualizationRootXAttrData), &data))
    {
        return true;
    }

    return false;
}

static void CombinePaths(const char* root, const char* relative, char (&combined)[PrjFSMaxPath])
{
    snprintf(combined, PrjFSMaxPath, "%s/%s", root, relative);
}

static bool SetBitInFileFlags(const char* fullPath, uint32_t bit, bool value)
{
    struct stat fileAttributes;
    if (lstat(fullPath, &fileAttributes))
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
    
    if (lchflags(fullPath, newValue))
    {
        return false;
    }
    
    return true;
}

static bool IsBitSetInFileFlags(const char* fullPath, uint32_t bit)
{
    struct stat fileAttributes;
    if (lstat(fullPath, &fileAttributes))
    {
        return false;
    }

    return fileAttributes.st_flags & bit;
}

static errno_t AddXAttr(const char* fullPath, const char* name, const void* value, size_t size)
{
    if (0 != setxattr(fullPath, name, value, size, 0, 0))
    {
        return errno;
    }
    
    return 0;
}

static bool TryGetXAttr(const char* fullPath, const char* name, size_t expectedSize, _Out_ void* value)
{
    if (expectedSize != getxattr(fullPath, name, value, expectedSize, 0, 0))
    {
        return false;
    }
    
    // TODO(Mac): also validate the magic number and format version.
    // It's easy to check their expected values, but we will need to decide what to do if they are incorrect.
    
    return true;
}

static errno_t RemoveXAttrWithoutFollowingLinks(const char* fullPath, const char* name)
{
    if (0 != removexattr(fullPath, name, XATTR_NOFOLLOW))
    {
        return errno;
    }

    return 0;
}

static inline PrjFS_NotificationType KUMessageTypeToNotificationType(MessageType kuNotificationType)
{
    switch(kuNotificationType)
    {
        case MessageType_KtoU_NotifyFileModified:
            return PrjFS_NotificationType_FileModified;
        
        case MessageType_KtoU_NotifyFilePreDelete:
        case MessageType_KtoU_NotifyDirectoryPreDelete:
            return PrjFS_NotificationType_PreDelete;

        case MessageType_KtoU_NotifyFilePreConvertToFull:
            return PrjFS_NotificationType_PreConvertToFull;
            
        case MessageType_KtoU_NotifyFileCreated:
            return PrjFS_NotificationType_NewFileCreated;
            
        case MessageType_KtoU_NotifyFileRenamed:
        case MessageType_KtoU_NotifyDirectoryRenamed:
            return PrjFS_NotificationType_FileRenamed;
            
        case MessageType_KtoU_NotifyFileHardLinkCreated:
            return PrjFS_NotificationType_HardLinkCreated;
        
        // Non-notification types
        case MessageType_Invalid:
        case MessageType_KtoU_EnumerateDirectory:
        case MessageType_KtoU_RecursivelyEnumerateDirectory:
        case MessageType_KtoU_HydrateFile:
        case MessageType_Response_Success:
        case MessageType_Response_Fail:
        case MessageType_Result_Aborted:
            return PrjFS_NotificationType_Invalid;
    }
}

static errno_t SendKernelMessageResponse(uint64_t messageId, MessageType responseType)
{
    const uint64_t inputs[] = { messageId, responseType };
    IOReturn callResult = IOConnectCallScalarMethod(
        s_kernelServiceConnection,
        ProviderSelector_KernelMessageResponse,
        inputs, extent<decltype(inputs)>::value, // scalar inputs
        nullptr, nullptr);                       // no outputs
    return callResult == kIOReturnSuccess ? 0 : EBADMSG;
}

static errno_t RegisterVirtualizationRootPath(const char* fullPath)
{
    uint64_t error = EBADMSG;
    uint32_t output_count = 1;
    size_t pathSize = strlen(fullPath) + 1;
    IOReturn callResult = IOConnectCallMethod(
        s_kernelServiceConnection,
        ProviderSelector_RegisterVirtualizationRootPath,
        nullptr, 0, // no scalar inputs
        fullPath, pathSize, // struct input
        &error, &output_count, // scalar output
        nullptr, nullptr); // no struct output
    assert(callResult == kIOReturnSuccess);
    return static_cast<errno_t>(error);
}

static PrjFS_Result RecursivelyMarkAllChildrenAsInRoot(const char* fullDirectoryPath)
{
    DIR* directory = nullptr;
    PrjFS_Result result = PrjFS_Result_Success;
    queue<string> directoryRelativePaths;
    directoryRelativePaths.push("");
    
    char fullPath[PrjFSMaxPath];
    char relativePath[PrjFSMaxPath];
    
    while (!directoryRelativePaths.empty())
    {
        string directoryRelativePath(directoryRelativePaths.front());
        directoryRelativePaths.pop();
        
        CombinePaths(fullDirectoryPath, directoryRelativePath.c_str(), fullPath);
        DIR* directory = opendir(fullPath);
        if (nullptr == directory)
        {
            result = PrjFS_Result_EIOError;
            goto CleanupAndReturn;
        }
        
        dirent* dirEntry = readdir(directory);
        while (dirEntry != nullptr)
        {
            bool entryIsDirectoryToUpdate = IsDirEntChildDirectory(dirEntry);
            if (entryIsDirectoryToUpdate || dirEntry->d_type == DT_LNK || dirEntry->d_type == DT_REG)
            {
                CombinePaths(directoryRelativePath.c_str(), dirEntry->d_name, relativePath);
                CombinePaths(fullDirectoryPath, relativePath, fullPath);
                if (!SetBitInFileFlags(fullPath, FileFlags_IsInVirtualizationRoot, true))
                {
                    result = PrjFS_Result_EIOError;
                    goto CleanupAndReturn;
                }
                
                if (entryIsDirectoryToUpdate)
                {
                    directoryRelativePaths.emplace(relativePath);
                }
            }
            
            dirEntry = readdir(directory);
        }
        
        closedir(directory);
    }
    
CleanupAndReturn:
    if (directory != nullptr)
    {
        closedir(directory);
    }
    
    return result;

}


#ifdef DEBUG
static const char* NotificationTypeToString(PrjFS_NotificationType notificationType)
{
    switch(notificationType)
    {
        case PrjFS_NotificationType_Invalid:
            return STRINGIFY(PrjFS_NotificationType_Invalid);

        case PrjFS_NotificationType_None:
            return STRINGIFY(PrjFS_NotificationType_None);
        case PrjFS_NotificationType_NewFileCreated:
            return STRINGIFY(PrjFS_NotificationType_NewFileCreated);
        case PrjFS_NotificationType_PreDelete:
            return STRINGIFY(PrjFS_NotificationType_PreDelete);
        case PrjFS_NotificationType_FileRenamed:
            return STRINGIFY(PrjFS_NotificationType_FileRenamed);
        case PrjFS_NotificationType_HardLinkCreated:
            return STRINGIFY(PrjFS_NotificationType_HardLinkCreated);
        case PrjFS_NotificationType_PreConvertToFull:
            return STRINGIFY(PrjFS_NotificationType_PreConvertToFull);
            
        case PrjFS_NotificationType_PreModify:
            return STRINGIFY(PrjFS_NotificationType_PreModify);
        case PrjFS_NotificationType_FileModified:
            return STRINGIFY(PrjFS_NotificationType_FileModified);
        case PrjFS_NotificationType_FileDeleted:
            return STRINGIFY(PrjFS_NotificationType_FileDeleted);
    }
}
#endif

static FileMutexMap::iterator CheckoutFileMutexIterator(const FsidInode& fsidInode)
{
    mutex_lock lock(s_fileLocksMutex);
    FileMutexMap::iterator iter = s_fileLocks.find(fsidInode);
    if (iter == s_fileLocks.end())
    {
        pair<FileMutexMap::iterator, bool> newEntry = s_fileLocks.insert(
            FileMutexMap::value_type(fsidInode, { make_shared<mutex>(), 1 }));
        assert(newEntry.second);
        return newEntry.first;
    }
    else
    {
        iter->second.useCount++;
        return iter;
    }
}

static void ReturnFileMutexIterator(FileMutexMap::iterator lockIterator)
{
    mutex_lock lock(s_fileLocksMutex);
    lockIterator->second.useCount--;
    if (lockIterator->second.useCount == 0)
    {
        s_fileLocks.erase(lockIterator);
    }
}

static const char* GetRelativePath(const char* fullPath, const char* root)
{
    size_t rootLength = strlen(root);
    size_t pathLength = strlen(fullPath);
    if (pathLength < rootLength || 0 != memcmp(fullPath, root, rootLength))
    {
        // TODO(Mac): Add this message to PrjFSLib logging once available (#395)
        fprintf(stderr, "GetRelativePath: root path '%s' is not a prefix of path '%s'\n", root, fullPath);
        return nullptr;
    }
    
    const char* relativePath = fullPath + rootLength;
    if (relativePath[0] == '/')
    {
        relativePath++;
    }
    else if (rootLength > 0 && root[rootLength - 1] != '/' && pathLength > rootLength)
    {
        // TODO(Mac): Add this message to PrjFSLib logging once available (#395)
        fprintf(stderr, "GetRelativePath: root path '%s' is not a parent directory of path '%s' (just a string prefix)\n", root, fullPath);
        return nullptr;
    }
    
    return relativePath;
}
