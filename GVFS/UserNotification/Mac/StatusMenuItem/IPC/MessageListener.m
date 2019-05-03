#import <netinet/in.h>
#import <sys/socket.h>
#import <sys/un.h>
#import "MessageListener.h"
#import "MessageParser.h"

const NSString * const NotificationServerPipeName = @"vfsforgit_native_notification_server";

@interface MessageListener ()

@property (strong) NSFileHandle *ConnectionHandle;
@property CFSocketRef SocketRef;

@end

@implementation MessageListener

- (id) initWithSocket:(NSString *) socketPath callback:(NewMessageCallback) callback
{
    if (self = [super init])
    {
        self.SocketPath = [socketPath stringByAppendingPathComponent:(NSString *) NotificationServerPipeName];
        self.MessageCallback = callback;
    }
    
    return self;
}

- (BOOL) StartListening
{
    CFSocketRef socketRef;
    if ((socketRef = CFSocketCreate(
        kCFAllocatorDefault,
        PF_LOCAL,
        SOCK_STREAM,
        0,
        kCFSocketNoCallBack,
        NULL,
        NULL)) == NULL)
    {
        return NO;
    }
    
    CFAutorelease(socketRef);
    
    int reuse = TRUE;
    int socketDescriptor = CFSocketGetNative(socketRef);
    if (setsockopt(socketDescriptor, SOL_SOCKET, SO_REUSEADDR, (void *) &reuse, sizeof(reuse)))
    {
        return NO;
    }
    
    if ([[NSFileManager defaultManager] fileExistsAtPath:self.SocketPath] &&
        ![[NSFileManager defaultManager] removeItemAtPath:self.SocketPath error:nil])
    {
        return NO;
    }
    
    struct sockaddr_un sockAddress = {};
    memset(&sockAddress, 0, sizeof(sockAddress));
    sockAddress.sun_family = AF_UNIX;
    sockAddress.sun_len = sizeof(sockAddress);
    [self.SocketPath getCString:sockAddress.sun_path maxLength:sizeof(sockAddress.sun_path) encoding:NSUTF8StringEncoding];
    CFDataRef addressData = CFDataCreate(kCFAllocatorDefault, (const UInt8 *) &sockAddress, sizeof(sockAddress));
    CFAutorelease(addressData);
    
    if (CFSocketSetAddress(socketRef, addressData) != kCFSocketSuccess)
    {
        return NO;
    }
    
    self.SocketRef = (CFSocketRef) CFRetain(socketRef);
    
    [[NSNotificationCenter defaultCenter]
        addObserver:self
        selector:@selector(connectionCallback:)
        name:NSFileHandleConnectionAcceptedNotification
        object:nil];
    
    self.ConnectionHandle = [[NSFileHandle alloc] initWithFileDescriptor:socketDescriptor closeOnDealloc:YES];
    [self.ConnectionHandle acceptConnectionInBackgroundAndNotify];
    
    return YES;
}

- (void) StopListening
{
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    
    CFRelease(self.SocketRef);
    self.SocketRef = NULL;
    self.ConnectionHandle = nil;
}

- (void) connectionCallback:(NSNotification *) notification
{
    @autoreleasepool
    {
        NSFileHandle *connectionHandle = nil;
        if ((connectionHandle = [[notification userInfo] objectForKey:NSFileHandleNotificationFileHandleItem]) != nil)
        {
            NSError *error;
            MessageParser *parser = [[MessageParser alloc] init];
            NSDictionary *message = [parser Parse:[connectionHandle availableData] error:&error];
            if (message != nil)
            {
                self.MessageCallback(message);
            }
            else
            {
                NSLog(@"ERROR: Could not parse notification: %@.", error ? [error description] : @"");
            }
        }
    
        [self.ConnectionHandle acceptConnectionInBackgroundAndNotify];
    }
}

@end
