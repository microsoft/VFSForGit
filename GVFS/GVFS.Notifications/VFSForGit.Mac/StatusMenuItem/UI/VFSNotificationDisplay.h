#import <Foundation/Foundation.h>

@interface VFSNotificationDisplay : NSObject

- (instancetype _Nullable)initWithTitle:(NSString *)title message:(NSString *)message;
- (instancetype _Nullable)initWithUserNotification:(NSUserNotification *)userNotification
                                notificationCenter:(NSUserNotificationCenter *)notificationCenter;
- (void) display;

@end
