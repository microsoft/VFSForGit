#import <Foundation/Foundation.h>
#import "VFSMockNotificationCenter.h"

@interface VFSMockNotificationCenter ()

@property (strong) NSUserNotification *expectedNotification;

@end

@implementation VFSMockNotificationCenter

- (instancetype) initWithExpectedNotification:(NSUserNotification *) notification
{
    if (self = [super init])
    {
        _expectedNotification = notification;
    }
    
    return self;
}

- (void)deliverNotification:(NSUserNotification *) notification
{
    if ([notification.title isEqualToString:self.expectedNotification.title] &&
        [notification.informativeText isEqualToString:self.expectedNotification.informativeText])
    {
        self.notificationDelivered = YES;
    }
}

@end
