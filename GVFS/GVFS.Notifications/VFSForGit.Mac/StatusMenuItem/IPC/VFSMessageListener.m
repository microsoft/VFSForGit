#import <netinet/in.h>
#import <sys/socket.h>
#import <sys/un.h>
#import "VFSMessageListener.h"
#import "VFSMessageParser.h"

NSString * const NotificationServerPipeName = @"vfsforgit.notification";

@interface VFSMessageListener ()

@property (strong) NSFileHandle *connectionHandle;
@property CFSocketRef socketRef;

@end

@implementation VFSMessageListener

- (instancetype)initWithSocket:(NSString *)socketPath
                      callback:(nonnull NewMessageCallback)callback
{
    if (self = [super init])
    {
        _socketPath =
        [[socketPath stringByAppendingPathComponent:NotificationServerPipeName]
         copy];
        _messageCallback = [callback copy];
    }
    
    return self;
}

- (void) dealloc
{
    if (_socketRef != NULL)
    {
        CFSocketInvalidate(_socketRef);
        CFRelease(_socketRef);
    }
}

- (BOOL)startListening
{
    CFSocketRef cfSocket;
    if ((cfSocket = CFSocketCreate(kCFAllocatorDefault,
                                   PF_LOCAL,
                                   SOCK_STREAM,
                                   0,
                                   kCFSocketNoCallBack,
                                   NULL,
                                   NULL)) == NULL)
    {
        return NO;
    }
    
    CFAutorelease(cfSocket);
    
    int reuse = TRUE;
    int socketDescriptor = CFSocketGetNative(cfSocket);
    if (setsockopt(socketDescriptor,
                   SOL_SOCKET,
                   SO_REUSEADDR,
                   (void *) &reuse,
                   sizeof(reuse)))
    {
        return NO;
    }
    
    if ([[NSFileManager defaultManager] fileExistsAtPath:self.socketPath] &&
        ![[NSFileManager defaultManager] removeItemAtPath:self.socketPath error:nil])
    {
        return NO;
    }
    
    struct sockaddr_un sockAddress = {};
    memset(&sockAddress, 0, sizeof(sockAddress));
    sockAddress.sun_family = AF_UNIX;
    sockAddress.sun_len = sizeof(sockAddress);
    [self.socketPath getCString:sockAddress.sun_path
                      maxLength:sizeof(sockAddress.sun_path)
                       encoding:NSUTF8StringEncoding];
    
    CFDataRef addressData = CFDataCreate(kCFAllocatorDefault,
                                         (const UInt8 *) &sockAddress,
                                         sizeof(sockAddress));
    CFAutorelease(addressData);
    if (CFSocketSetAddress(cfSocket, addressData) != kCFSocketSuccess)
    {
        return NO;
    }
    
    self.socketRef = (CFSocketRef) CFRetain(cfSocket);
    
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(messageReadCompleteCallback:)
                                                 name:NSFileHandleReadToEndOfFileCompletionNotification
                                               object:nil];
    
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(newConnectionCallback:)
                                                 name:NSFileHandleConnectionAcceptedNotification
                                               object:nil];
    
    self.connectionHandle = [[NSFileHandle alloc] initWithFileDescriptor:socketDescriptor
                                                          closeOnDealloc:YES];
    [self.connectionHandle acceptConnectionInBackgroundAndNotify];
    
    return YES;
}

- (void)stopListening
{
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    
    CFSocketInvalidate(self.socketRef);
    CFRelease(self.socketRef);
    
    self.socketRef = NULL;
    self.connectionHandle = nil;
}

- (void)newConnectionCallback:(NSNotification *)notification
{
    @autoreleasepool
    {
        NSFileHandle *readHandle = [[notification userInfo]
                                    objectForKey:NSFileHandleNotificationFileHandleItem];
        if (readHandle != nil)
        {
            [readHandle readToEndOfFileInBackgroundAndNotify];
        }
        
        [self.connectionHandle acceptConnectionInBackgroundAndNotify];
    }
}

- (void)messageReadCompleteCallback:(NSNotification *)notification
{
    @autoreleasepool
    {
        NSData *data = [[notification userInfo] objectForKey:NSFileHandleNotificationDataItem];
        if ((data != nil) && ([data length] > 0))
        {
            NSError *error;
            NSDictionary *message;
            if([VFSMessageParser tryParseData:data message:&message error:&error])
            {
                self.messageCallback(message);
            }
            else
            {
                NSLog(@"ERROR: Could not parse notification payload: %@.", [error description]);
            }
        }
        else
        {
            NSNumber *unixError = [[notification userInfo] objectForKey:@"NSFileHandleError"];
            NSLog(@"ERROR: Could not read data from socket %s.",
                  unixError != nil ? strerror([unixError intValue]) : "");
        }
    }
}

@end
