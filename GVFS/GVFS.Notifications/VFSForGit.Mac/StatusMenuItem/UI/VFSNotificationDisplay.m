#import <Cocoa/Cocoa.h>
#import "VFSNotificationDisplay.h"

@interface VFSNotificationDisplay ()

@property (strong) NSUserNotification *userNotification;
@property (strong) NSUserNotificationCenter *userNotificationCenter;

@end

@implementation VFSNotificationDisplay

- (instancetype)initWithTitle:(NSString *)title message:(NSString *)message
{
    NSUserNotification *userNotification = [[NSUserNotification alloc] init];
    userNotification.title = title;
    userNotification.informativeText = message;
    NSUserNotificationCenter *notificationCenter =
    [NSUserNotificationCenter defaultUserNotificationCenter];
    
    return self = [self initWithUserNotification:userNotification
                              notificationCenter:notificationCenter];
}

- (instancetype)initWithUserNotification:(NSUserNotification *)userNotification
                      notificationCenter:(NSUserNotificationCenter *)notificationCenter
{
    if (self = [super init])
    {
        _userNotification = userNotification;
        _userNotificationCenter = notificationCenter;
    }
    
    return self;
}

- (void)display
{
    [self.userNotificationCenter deliverNotification:self.userNotification];
}

@end
