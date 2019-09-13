#import <Foundation/Foundation.h>
#import "VFSForGitNotification.h"

@interface VFSNotificationDisplay : NSObject <NSUserNotificationCenterDelegate>

- (instancetype)initWithCommandRunner:(VFSCommandRunner *)commandRunner;
- (void)display:(VFSForGitNotification *) notification;

@end
