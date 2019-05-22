#import "VFSMessageParser.h"
#import "VFSNotificationErrors.h"

NSString * const NotificationPrefix = @"Notification|";

@implementation VFSMessageParser

+ (BOOL)tryParseData:(NSData *)data
             message:(NSDictionary *__autoreleasing *)parsedMessage
               error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(parsedMessage);
    
    NSString *messageStr;
    if (!(messageStr = [[NSString alloc] initWithData:data
                                             encoding:NSUTF8StringEncoding]))
    {
        if (error != nil)
        {
            NSString *info = [NSString stringWithFormat:@"%@: ERROR: error reading data.",
                              NSStringFromSelector(_cmd)];
            *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                         code:VFSForGitMessageReadError
                                     userInfo:@{ NSLocalizedDescriptionKey : info }];
        }
        *parsedMessage = nil;
        return NO;
    }
    
    if ([messageStr hasPrefix:NotificationPrefix])
    {
        messageStr = [messageStr substringFromIndex:[NotificationPrefix length]];
    }
    
    messageStr = [messageStr stringByTrimmingCharactersInSet:[NSCharacterSet controlCharacterSet]];
    
    NSError *parseError;
    if (!(*parsedMessage = [NSJSONSerialization
                            JSONObjectWithData:[messageStr dataUsingEncoding:NSUTF8StringEncoding]
                            options:NSJSONReadingAllowFragments
                            error:&parseError]))
    {
        if (error != nil)
        {
            if (parseError == nil)
            {
                NSString *info = [NSString stringWithFormat:@"%@: ERROR: Unknown parse error.",
                                  NSStringFromSelector(_cmd)];
                *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                             code:VFSForGitMessageParseError
                                         userInfo:@{ NSLocalizedDescriptionKey : info }];
            }
            else
            {
                *error = parseError;
            }
        }
    }
    
    return *parsedMessage != nil;
}

@end
