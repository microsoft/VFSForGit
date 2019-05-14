#import <Cocoa/Cocoa.h>
#import "VFSUserNotification.h"

@interface VFSUserNotification ()

@property (copy) NSString *title;
@property (copy) NSString *message;

@end

// Placholder class for actually displaying
// notifications.
// TODO : Hook this up with message listener
// to display notifications.

@implementation VFSUserNotification

- (instancetype)initWithInfo:(NSDictionary<NSString *, NSString*> *)info
{
    if (info == nil)
    {
        self = nil;
    }
    else if (self = [super init])
    {
        _title = [[info objectForKey:@"Title"] copy];
        _message = [[info objectForKey:@"Message"] copy];
    }
    
    return self;
}

- (void)display
{
    NSUserNotification *notification = [[NSUserNotification alloc] init];
    notification.title = self.title;
    notification.informativeText = self.message;
    notification.soundName = NSUserNotificationDefaultSoundName;
    
    [[NSUserNotificationCenter defaultUserNotificationCenter]
     deliverNotification:notification];
}

@end
