#import <Foundation/Foundation.h>

typedef void (^NewMessageCallback) (NSDictionary *messageInfo);

@interface MessageListener : NSObject

@property (copy) NSString *SocketPath;
@property (copy) NewMessageCallback MessageCallback;

- (id) initWithSocket:(NSString *) socketPath callback:(NewMessageCallback) callback;
- (BOOL) StartListening;
- (void) StopListening;

@end
