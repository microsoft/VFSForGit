#import <Foundation/Foundation.h>

#ifdef __cplusplus
extern "C"
{
#endif
    
    typedef enum
    {
        Info,
        Warning,
        Error
    } NotificationType;
    
    int DisplayNotification(
        NotificationType notificationType,
        char *title,
        char *message,
        char *defaultActionName,
        char *cancelActionName,
        char *defaultCommand,
        char *defaultCommandArgs,
        char *cancelCommand,
        char *cancelCommandArgs);
    
#ifdef __cplusplus
} // extern "C"
#endif

@interface NativeNotification : NSObject

+ (BOOL) DisplayNotification:(NotificationType) type
                       title:(NSString *) title
                     message:(NSString *) message
           defaultActionName:(NSString *) actionMessage
            cancelActionName:(NSString *) cancelMessage
              defaultCommand:(NSString *) defaultCommand
          defaultCommandArgs:(NSString *) defaultCommandArgs
               cancelCommand:(NSString *) cancelCommand
           cancelCommandArgs:(NSString *) cancelCommandArgs;
@end
