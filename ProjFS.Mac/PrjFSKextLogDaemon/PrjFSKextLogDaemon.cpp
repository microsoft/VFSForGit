#include "../PrjFSKext/public/PrjFSLogClientShared.h"
#include "../PrjFSKext/public/PrjFSVnodeCacheHealth.h"
#include "../PrjFSLib/Json/JsonWriter.hpp"
#include "../PrjFSLib/PrjFSUser.hpp"
#include <atomic>
#include <dirent.h>
#include <iostream>
#include <mutex>
#include <sstream>
#include <OS/log.h>
#include <IOKit/IOKitLib.h>
#include <signal.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/un.h>
#include <unistd.h>

using std::atomic_exchange;
using std::atomic_uint32_t;
using std::hex;
using std::lock_guard;
using std::mutex;
using std::ostringstream;
using std::string;

static const char PrjFSKextLogDaemon_OSLogSubsystem[] = "org.vfsforgit.prjfs.PrjFSKextLogDaemon";
static const char PanicLogDirectory[] = "/Library/Logs/DiagnosticReports";
static const char PanicLogTimestampDirectory[] = "/usr/local/vfsforgit/diagnostics";
static const char PanicLogTimestampFile[] = "/usr/local/vfsforgit/diagnostics/LastRun.txt";
static const int INVALID_SOCKET_FD = -1;

static os_log_t s_daemonLogger, s_kextLogger;
static IONotificationPortRef s_notificationPort;

static int s_messageListenerSocket = INVALID_SOCKET_FD;
static const char MessageListenerSocketPath[] = "/usr/local/GitService/pipe/vfs-c780ac06-135a-4e9e-ab6c-d41e2d265baa";
static const string MessageKey = "message";
enum class MessageType
{
    Info,
    Error,
    VnodeCacheHealth
};

static mutex s_messageListenerMutex;

static atomic_uint32_t s_droppedMessageCount(0);

static void LogPanics();
static long GetFileModifiedTime(const char* path);
static bool DoesPathExist(const char* path);
static void StartLoggingKextMessages(io_connect_t connection, io_service_t service);
static void HandleSigterm(int sig, siginfo_t* info, void* uc);
static void SetupExitSignalHandler();

static dispatch_source_t StartPeriodicLoggingTimer(io_connect_t connection);
static void FetchAndLogKextHealthData(io_connect_t connection);

static void ReportDroppedKextMessages();

// LogXXX functions will log to the OS as well as the message listener
static void LogDaemonError(const string& message);
static void LogKextMessage(os_log_type_t messageLogType, uint32_t messageFlags, const char* message, int messageLength);
static void LogKextHealthData(const PrjFSVnodeCacheHealth& healthData);

static void CreatePipeToMessageListener();
static void ClosePipeToMessageListener_Locked();
static string MessageTypeToString(MessageType messageType);
static void WriteJsonToMessageListener(MessageType messageType, const JsonWriter& jsonMessage);

int main(int argc, const char* argv[])
{
#ifdef DEBUG
    printf("PrjFSKextLogDaemon starting up");
    fflush(stdout);
#endif
    
    s_daemonLogger = os_log_create(PrjFSKextLogDaemon_OSLogSubsystem, "daemon");
    s_kextLogger = os_log_create(PrjFSKextLogDaemon_OSLogSubsystem, "kext");
    
    os_log(s_daemonLogger, "PrjFSKextLogDaemon starting up");
    
    CreatePipeToMessageListener();
    JsonWriter messageWriter;
    messageWriter.Add(MessageKey, "PrjFSKextLogDaemon starting up");
    WriteJsonToMessageListener(MessageType::Info, messageWriter);

    s_notificationPort = IONotificationPortCreate(kIOMasterPortDefault);
    IONotificationPortSetDispatchQueue(s_notificationPort, dispatch_get_main_queue());

    PrjFSService_WatchContext* watchContext = PrjFSService_WatchForServiceAndConnect(
         s_notificationPort,
         UserClientType_Log,
         [](io_service_t service, io_connect_t connection, bool serviceVersionMismatch, IOReturn connectResult, PrjFSService_WatchContext* context)
         {
             if (connectResult != kIOReturnSuccess || connection == IO_OBJECT_NULL)
             {
                io_string_t servicePath = "";
                IORegistryEntryGetPath(service, kIOServicePlane, servicePath);
                if (serviceVersionMismatch)
                {
                    CFTypeRef kextVersionObj = IORegistryEntryCreateCFProperty(service, CFSTR(PrjFSKextVersionKey), kCFAllocatorDefault, 0);
                    os_log_error(
                        s_daemonLogger,
                        "Failed to connect to newly matched PrjFS kernel service at '%{public}s'; version mismatch. Expected %{public}s, kernel service version %{public}@",
                        servicePath, PrjFSKextVersion, kextVersionObj);
                    
                    JsonWriter messageWriter;
                    messageWriter.Add(MessageKey, "Failed to connect to newly matched PrjFS kernel service at '" + string(servicePath) + "'; version mismatch.");
                    WriteJsonToMessageListener(MessageType::Error, messageWriter);
    
                    if (kextVersionObj != nullptr)
                    {
                        CFRelease(kextVersionObj);
                    }
                }
                else
                {
                    ostringstream errorMessage;
                    errorMessage << "Failed to connect to newly matched PrjFS kernel service at '" << servicePath << "'; connecting failed with error 0x" << hex << connectResult;
                    LogDaemonError(errorMessage.str());
                }
             }
             else
             {
                 StartLoggingKextMessages(connection, service);
             }
         });
    if (nullptr == watchContext)
    {
        LogDaemonError("PrjFSKextLogDaemon failed to register for service notifications.");
        return 1;
    }
    
    SetupExitSignalHandler();

    os_log(s_daemonLogger, "PrjFSKextLogDaemon running");

    LogPanics();
    CFRunLoopRun();

    os_log(s_daemonLogger, "PrjFSKextLogDaemon shutting down");

    PrjFSService_StopWatching(watchContext);
    
    {
        lock_guard<mutex> lock(s_messageListenerMutex);
        ClosePipeToMessageListener_Locked();
    }
    
    return 0;
}

static os_log_type_t KextLogLevelAsOSLogType(KextLog_Level level)
{
    switch (level)
    {
    case KEXTLOG_INFO:
        return OS_LOG_TYPE_INFO;
    case KEXTLOG_DEFAULT:
        return OS_LOG_TYPE_DEFAULT;
    case KEXTLOG_ERROR:
    default:
        return OS_LOG_TYPE_ERROR;
    }
}

static void LogPanics()
{
    DIR* dr = opendir(PanicLogDirectory);
    if (dr == nullptr)
    {
        ostringstream errorMessage;
        errorMessage << "Unable to open " << PanicLogDirectory << ", errno=" << errno << ", errorstr=" << strerror(errno);
        LogDaemonError(errorMessage.str());
        return;
    }

    // Look up the last run time
    long lastRunTimestamp = GetFileModifiedTime(PanicLogTimestampFile);

    struct dirent* dirEntry;
    while ((dirEntry = readdir(dr)) != nullptr)
    {
        // Only check files
        if(dirEntry->d_type == DT_REG)
        {
           // Check for .panic extension
           const char* ext = strrchr(dirEntry->d_name,'.');
           if(ext != nullptr && strcmp(ext, ".panic") == 0)
           {
               // Get the full name of the panic file
               string fullFileName = PanicLogDirectory;
               fullFileName += "/";
               fullFileName += dirEntry->d_name;

               // Verify it's after the last timestamp
               long panicLogTime = GetFileModifiedTime(fullFileName.c_str());
               if (panicLogTime == 0)
               {
                   // Record error if we can't determine timestamp
                   os_log(s_daemonLogger, "Unable to run stat on panic at %s errno=%d strerror=%s\n", fullFileName.c_str(), errno, strerror(errno));
                   continue;
               }

               if (panicLogTime >= lastRunTimestamp)
               {
                  // Check if 'org.vfsforgit.PrjFSKext(' occurs in the log
                  // If it does, then PrjFSKext may have caused the panic and we should log an error
                  FILE* fp = fopen(fullFileName.c_str(), "r");
                  if (fp != nullptr)
                  {
                      char* line = nullptr;
                      size_t len = 0;
                      ssize_t read;
                      while ((read = getline(&line, &len, fp)) != -1)
                      {
                          if (strstr(line, "org.vfsforgit.PrjFSKext(") != nullptr)
                          {
                              ostringstream errorMessage;
                              errorMessage << "Found panic at " << fullFileName.c_str() << "\n";
                              LogDaemonError(errorMessage.str());
                              break;
                          }
                      }
                      
                      fclose(fp);
                      free(line);
                  }
                  else
                  {
                      os_log(s_daemonLogger, "Unable open %s errno=%d strerror=%s\n", fullFileName.c_str(), errno, strerror(errno));
                      continue;
                  }
              }
           }
        }
    }
    closedir(dr);

    // Update timestamp file
    if (!DoesPathExist(PanicLogTimestampDirectory))
    {
        mkdir(PanicLogTimestampDirectory, 0755);
    }
    
    FILE* fp = fopen(PanicLogTimestampFile, "a");
    if (fp != nullptr)
    {
       fprintf(fp, "Completed Panic Sweep for :%ld\n", lastRunTimestamp);
       fclose(fp);
    }
    else
    {
        os_log(s_daemonLogger, "Unable to save panic timestamp file %s errno=%d strerror=%s\n", PanicLogTimestampFile, errno, strerror(errno));
    }
}

static long GetFileModifiedTime(const char* path)
{
    struct stat attr;
    if (stat(path, &attr) == -1)
    {
        return 0;
    }
    return attr.st_mtimespec.tv_sec;
}

static bool DoesPathExist(const char* path)
{
    struct stat attr;
    if (stat(path, &attr) == -1)
    {
        return false;
    }
    return true;
}

static void StartLoggingKextMessages(io_connect_t connection, io_service_t prjfsService)
{
    uint64_t prjfsServiceEntryID = 0;
    IORegistryEntryGetRegistryEntryID(prjfsService, &prjfsServiceEntryID);

    std::shared_ptr<DataQueueResources> logDataQueue(new DataQueueResources {});
    if (!PrjFSService_DataQueueInit(logDataQueue.get(), connection, LogPortType_MessageQueue, LogMemoryType_MessageQueue, dispatch_get_main_queue()))
    {
        ostringstream errorMessage;
        errorMessage << "StartLoggingKextMessages: Failed to set up log message data queue an connection to service with registry entry 0x" << hex << prjfsServiceEntryID;
        LogDaemonError(errorMessage.str());
        IOServiceClose(connection);
        return;
    }

    os_log(s_daemonLogger, "Started logging kext messages from PrjFS IOService with registry entry id 0x%llx", prjfsServiceEntryID);

    dispatch_source_set_event_handler(logDataQueue->dispatchSource, ^{
        DataQueue_ClearMachNotification(logDataQueue->notificationPort);
        
        while (IODataQueueEntry* entry = DataQueue_Peek(logDataQueue->queueMemory))
        {
            int messageSize = entry->size;
            if (messageSize >= sizeof(KextLog_MessageHeader) + 2)
            {
                struct KextLog_MessageHeader message = {};
                memcpy(&message, entry->data, sizeof(KextLog_MessageHeader));
                int logStringLength = messageSize - sizeof(KextLog_MessageHeader) - 1;
                os_log_type_t messageLogType = KextLogLevelAsOSLogType(message.level);
                LogKextMessage(
                    messageLogType,
                    message.flags,
                    reinterpret_cast<char*>(entry->data + sizeof(KextLog_MessageHeader)),
                    logStringLength);
            }
            else
            {
                ostringstream errorMessage;
                errorMessage << "StartLoggingKextMessages: Malformed message received from kext. messageSize = " << messageSize
                             << ", expecting " << sizeof(KextLog_MessageHeader) + 2 << "or more";
                LogDaemonError(errorMessage.str());
            }

            DataQueue_Dequeue(logDataQueue->queueMemory, nullptr, nullptr);
        }
    });
    dispatch_resume(logDataQueue->dispatchSource);

    dispatch_source_t timer = StartPeriodicLoggingTimer(connection);

    PrjFSService_WatchForServiceTermination(
        prjfsService,
        s_notificationPort,
        [prjfsServiceEntryID, connection, logDataQueue, timer]()
        {
            DataQueue_Dispose(logDataQueue.get(), connection, LogMemoryType_MessageQueue);

            if (nullptr != timer)
            {
                dispatch_cancel(timer);
                dispatch_release(timer);
            }

            IOServiceClose(connection);
            os_log(s_daemonLogger, "Stopped logging kext messages from PrjFS IOService with registry entry id 0x%llx", prjfsServiceEntryID);
        });
}

static void HandleSigterm(int sig, siginfo_t* info, void* uc)
{
    // Does nothing, unlike the default implementation which immediately aborts exits the process
}

// Sets up handling of SIGTERM so that shutting down the daemon can be logged (at the end of main())
// This is for clarity, so that we know if absence of logs is because there was nothing logged, or because the log daemon shut down.
static void SetupExitSignalHandler()
{
    dispatch_source_t signalSource =
        dispatch_source_create(DISPATCH_SOURCE_TYPE_SIGNAL, SIGTERM, 0 /* no mask values for signal dispatch sources */, dispatch_get_main_queue());
    dispatch_source_set_event_handler(signalSource, ^{
        CFRunLoopStop(CFRunLoopGetMain());
    });
    dispatch_resume(signalSource);
    
    struct sigaction newAction = { .sa_sigaction = HandleSigterm };
    struct sigaction oldAction = {};
    sigaction(SIGTERM, &newAction, &oldAction);
}

static dispatch_source_t StartPeriodicLoggingTimer(io_connect_t connection)
{
    dispatch_source_t timer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, dispatch_get_main_queue());
    dispatch_source_set_timer(
        timer,
        DISPATCH_TIME_NOW,      // start
        30 * 60 * NSEC_PER_SEC, // interval
        10 * NSEC_PER_SEC);     // leeway
    dispatch_source_set_event_handler(timer, ^{
        // Every time the timer fires attempt to connect (if not already connected)
        CreatePipeToMessageListener();
        FetchAndLogKextHealthData(connection);
        ReportDroppedKextMessages();
    });
    dispatch_resume(timer);
    return timer;
}

static void FetchAndLogKextHealthData(io_connect_t connection)
{
    PrjFSVnodeCacheHealth healthData;
    size_t out_size = sizeof(healthData);
    IOReturn ret = IOConnectCallStructMethod(connection, LogSelector_FetchVnodeCacheHealth, nullptr, 0, &healthData, &out_size);
    if (ret == kIOReturnUnsupported)
    {
        LogDaemonError("FetchAndLogKextHealthData: IOConnectCallStructMethod failed for LogSelector_FetchVnodeCacheHealth, ret: kIOReturnUnsupported");
    }
    else if (ret == kIOReturnSuccess)
    {
        LogKextHealthData(healthData);
    }
    else
    {
        ostringstream errorMessage;
        errorMessage << "FetchAndLogKextHealthData: Fetching profiling data from kernel failed, ret: 0x" << hex << ret;
        LogDaemonError(errorMessage.str());
    }
}

static void ReportDroppedKextMessages()
{
    // We should only report the number of drops reported during the last time interval (i.e. since the last
    // time ReportDroppedKextMessages was called).  Capture the current value of s_droppedMessageCount and reset it to 0.
    uint32_t droppedMessageCount = atomic_exchange(&s_droppedMessageCount, 0U);
    
    if (droppedMessageCount > 0)
    {
        ostringstream message;
        message << "ReportAnyDroppedMessages: " << droppedMessageCount << " reports of dropped kext log messages since last checked";
        LogDaemonError(message.str());
    }
}

static void LogDaemonError(const string& message)
{
    os_log_error(s_daemonLogger, "%{public}s", message.c_str());

    JsonWriter messageWriter;
    messageWriter.Add(MessageKey, message);
    WriteJsonToMessageListener(MessageType::Error, messageWriter);
}

static void LogKextMessage(os_log_type_t messageLogType, uint32_t messageFlags, const char* message, int messageLength)
{
    if (messageFlags & LogMessageFlag_LogMessagesDropped)
    {
        if (1 == ++s_droppedMessageCount)
        {
            // Don't log every time a message is dropped to avoid flooding the logs under heavy load
            LogDaemonError("LogKextMessage: One or more kext log messages have been dropped");
        }
    }
    
    os_log_with_type(
        s_kextLogger,
        messageLogType,
        "%{public}s%{public}.*s",
        (messageFlags & LogMessageFlag_LogMessageTruncated) ? "TRUNCATED: " : "",
        messageLength,
        message);
    
    if (OS_LOG_TYPE_ERROR == messageLogType)
    {
        JsonWriter messageWriter;
        messageWriter.Add(MessageKey, string(message, messageLength));
        messageWriter.Add("truncated", (messageFlags & LogMessageFlag_LogMessageTruncated) == LogMessageFlag_LogMessageTruncated);
        WriteJsonToMessageListener(MessageType::Error, messageWriter);
    }
}

static void LogKextHealthData(const PrjFSVnodeCacheHealth& healthData)
{
    os_log(
        s_kextLogger,
        "PrjFS Vnode Cache Health: CacheCapacity=%u, CacheEntries=%u, InvalidationCount=%llu, CacheLookups=%llu, LookupCollisions=%llu, FindRootHits=%llu, FindRootMisses=%llu, RefreshRoot=%llu, InvalidateRoot=%llu",
        healthData.cacheCapacity,
        healthData.cacheEntries,
        healthData.invalidateEntireCacheCount,
        healthData.totalCacheLookups,
        healthData.totalLookupCollisions,
        healthData.totalFindRootForVnodeHits,
        healthData.totalFindRootForVnodeMisses,
        healthData.totalRefreshRootForVnode,
        healthData.totalInvalidateVnodeRoot);
    
    JsonWriter healthDataWriter;
    healthDataWriter.Add("CacheCapacity", healthData.cacheCapacity);
    healthDataWriter.Add("CacheEntries", healthData.cacheEntries);
    healthDataWriter.Add("InvalidationCount", healthData.invalidateEntireCacheCount);
    healthDataWriter.Add("CacheLookups", healthData.totalCacheLookups);
    healthDataWriter.Add("LookupCollisions", healthData.totalLookupCollisions);
    healthDataWriter.Add("FindRootHits", healthData.totalFindRootForVnodeHits);
    healthDataWriter.Add("FindRootMisses", healthData.totalFindRootForVnodeMisses);
    healthDataWriter.Add("RefreshRoot", healthData.totalRefreshRootForVnode);
    healthDataWriter.Add("InvalidateRoot", healthData.totalInvalidateVnodeRoot);
    WriteJsonToMessageListener(MessageType::VnodeCacheHealth, healthDataWriter);
}

static void CreatePipeToMessageListener()
{
    lock_guard<mutex> lock(s_messageListenerMutex);
    
    if (INVALID_SOCKET_FD != s_messageListenerSocket)
    {
        // Already connected
        return;
    }

    int error = 0;

    s_messageListenerSocket = socket(PF_UNIX, SOCK_STREAM, 0);
    if (s_messageListenerSocket < 0)
    {
        error = errno;
        os_log(s_daemonLogger, "CreatePipeToMessageListener: Failed to create socket, error: %d", error);
        s_messageListenerSocket = INVALID_SOCKET_FD;
        return;
    }
    
    struct sockaddr_un socket_address;
    memset(&socket_address, 0, sizeof(struct sockaddr_un));
    
    socket_address.sun_family = AF_UNIX;
    
    static_assert(
        sizeof(MessageListenerSocketPath) <= sizeof(socket_address.sun_path),
        "socket_address.sun_path is not large enough for MessageListenerSocketPath");
    strlcpy(socket_address.sun_path, MessageListenerSocketPath, sizeof(socket_address.sun_path));
    
    if(0 == connect(s_messageListenerSocket, (struct sockaddr *) &socket_address, sizeof(struct sockaddr_un)))
    {
        os_log(
            s_daemonLogger,
            "CreatePipeToMessageListener: Connected to message listener at %{public}s",
            MessageListenerSocketPath);
        return;
    }
    
    error = errno;
    os_log(
        s_daemonLogger,
        "CreatePipeToMessageListener: Failed to connect socket at %{public}s, error: %d",
        MessageListenerSocketPath,
        error);
    
    ClosePipeToMessageListener_Locked();
}

static void ClosePipeToMessageListener_Locked()
{
    if (INVALID_SOCKET_FD != s_messageListenerSocket)
    {
        close(s_messageListenerSocket);
        s_messageListenerSocket = INVALID_SOCKET_FD;
    }
}

static string MessageTypeToString(MessageType messageType)
{
    switch (messageType) {
        case MessageType::Info:
            return "info";
            
        case MessageType::Error:
            return "error";
            
        case MessageType::VnodeCacheHealth:
            return "health.vnodeCache";
    }
    
    return "invalid";
}

static void WriteJsonToMessageListener(MessageType messageType, const JsonWriter& jsonMessage)
{
    lock_guard<mutex> lock(s_messageListenerMutex);
    
    if (INVALID_SOCKET_FD == s_messageListenerSocket)
    {
        return;
    }

    JsonWriter fullMessageWriter;
    fullMessageWriter.Add("version", PrjFSKextVersion);
    fullMessageWriter.Add("providerName", "Microsoft.Git.GVFS");
    
    
    fullMessageWriter.Add("eventName", "kext." + MessageTypeToString(messageType));
    fullMessageWriter.Add("payload", jsonMessage);
    
    string fullMessage = fullMessageWriter.ToString() + "\n";
    size_t bytesRemaining = fullMessage.length();
    while (bytesRemaining > 0)
    {
        size_t offset = fullMessage.length() - bytesRemaining;
        ssize_t bytesWritten = write(s_messageListenerSocket, fullMessage.c_str() + offset, bytesRemaining);
       
        if (-1 == bytesWritten)
        {
            if (EINTR != errno)
            {
                break;
            }
        }
        else
        {
            bytesRemaining -= bytesWritten;
        }
    }
    
    int error = errno;
    if (bytesRemaining != 0)
    {
        os_log(
            s_daemonLogger,
            "WriteJsonToMessageListener: Failed to write message '%{public}s' to listener. Error: %d, Bytes remaining: %zu",
            fullMessage.c_str(),
            error,
            bytesRemaining);

        // If anything goes wrong close the socket.  The next time the timer fires we'll re-connect
        ClosePipeToMessageListener_Locked();
    }
}
