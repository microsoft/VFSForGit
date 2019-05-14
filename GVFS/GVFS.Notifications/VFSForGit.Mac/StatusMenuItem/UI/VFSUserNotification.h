#import <Foundation/Foundation.h>

@interface VFSUserNotification : NSObject

- (instancetype _Nullable)initWithInfo:(NSDictionary<NSString *, NSString*> *_Nonnull)info;
- (void)display;

@end
