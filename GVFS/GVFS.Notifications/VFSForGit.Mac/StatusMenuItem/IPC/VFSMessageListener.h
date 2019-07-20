#import <Foundation/Foundation.h>

typedef void (^NewMessageCallback) (NSDictionary *_Nonnull messageInfo);

NS_ASSUME_NONNULL_BEGIN

@interface VFSMessageListener : NSObject

@property (copy) NSString *socketPath;
@property (copy) NewMessageCallback messageCallback;

- (instancetype _Nullable)initWithSocket:(NSString *)socketPath
                                callback:(NewMessageCallback)callback;
- (BOOL)startListening;
- (void)stopListening;

@end

NS_ASSUME_NONNULL_END
