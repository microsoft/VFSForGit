#import <Cocoa/Cocoa.h>
#import "VFSCommandRunner.h"
#import "VFSNotificationDisplay.h"

@interface VFSNotificationDisplay ()

@property (strong, nonnull) VFSCommandRunner *commandRunner;

@end

@implementation VFSNotificationDisplay

- (instancetype)initWithCommandRunner:(VFSCommandRunner *)commandRunner
{
    if (self = [super init])
    {
        _commandRunner = commandRunner;
        [[NSUserNotificationCenter defaultUserNotificationCenter] setDelegate:self];
    }
    
    return self;
}

- (void)display:(VFSForGitNotification *) notification
{
    NSUserNotification *userNotification = [[NSUserNotification alloc] init];
    userNotification.title = notification.title;
    userNotification.informativeText = notification.message;
    userNotification.userInfo = [NSDictionary dictionaryWithObject:[NSKeyedArchiver archivedDataWithRootObject:notification]
                                                            forKey:@"VFSForGitNotification"];
    if (notification.actionable == YES)
    {
        userNotification.hasActionButton = notification.actionable;
        userNotification.actionButtonTitle = notification.actionTitle;
    }
    
    [[NSUserNotificationCenter defaultUserNotificationCenter] deliverNotification:userNotification];
}

- (void)userNotificationCenter:(NSUserNotificationCenter *)center
       didActivateNotification:(NSUserNotification *)notification
{
    [[NSUserNotificationCenter defaultUserNotificationCenter] removeDeliveredNotification:notification];
    
    VFSForGitNotification *vfsNotification = [NSKeyedUnarchiver
                                              unarchiveObjectWithData:[[notification userInfo]
                                                                       objectForKey:@"VFSForGitNotification"]];
    if (vfsNotification != nil)
    {
        switch (notification.activationType)
        {
            case NSUserNotificationActivationTypeActionButtonClicked:
            {
                if (vfsNotification.gvfsCommand)
                {
                    [self.commandRunner runCommand:vfsNotification.gvfsCommand];
                }
                
                break;
            }
                
            default:
            {
                break;
            }
        }
    }    
}

@end
