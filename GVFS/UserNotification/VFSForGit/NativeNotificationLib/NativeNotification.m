#import "NativeNotification.h"

int DisplayNotification(
                        NotificationType notificationType,
                        char *title,
                        char *message,
                        char *defaultActionName,
                        char *cancelActionName,
                        char *defaultCommand,
                        char *defaultCommandArgs,
                        char *cancelCommand,
                        char *cancelCommandArgs)
{
    return [NativeNotification DisplayNotification:notificationType
                                             title:[NSString stringWithUTF8String:title]
                                           message:[NSString stringWithUTF8String:message]
                                 defaultActionName:[NSString stringWithUTF8String:defaultActionName]
                                  cancelActionName:[NSString stringWithUTF8String:cancelActionName]
                                    defaultCommand:[NSString stringWithUTF8String:defaultCommand]
                                defaultCommandArgs:[NSString stringWithUTF8String:defaultCommandArgs]
                                     cancelCommand:[NSString stringWithUTF8String:cancelCommand]
                                 cancelCommandArgs:[NSString stringWithUTF8String:cancelCommandArgs]];
}

@implementation NativeNotification

+ (BOOL) DisplayNotification:(NotificationType) type
                       title:(NSString *) title
                     message:(NSString *) message
           defaultActionName:(NSString *) actionMessage
            cancelActionName:(NSString *) cancelMessage
              defaultCommand:(NSString *) defaultCommand
          defaultCommandArgs:(NSString *) defaultCommandArgs
               cancelCommand:(NSString *) cancelCommand
           cancelCommandArgs:(NSString *) cancelCommandArgs
{
    @autoreleasepool
    {
        [[NSDistributedNotificationCenter defaultCenter] postNotificationName:@"VFSForGitPlatformNativeNotification"
                                                                       object:NULL
                                                                     userInfo:[NSDictionary dictionaryWithObjectsAndKeys:title, @"title", message, @"message", nil]];
    }
    
    return YES;
}

@end
