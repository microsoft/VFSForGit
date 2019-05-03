#import "MessageParser.h"

const NSString * const MessagePrefix = @"Notification|";

@implementation MessageParser

- (NSDictionary *) Parse:(NSData *) data error:(NSError **) error
{
    NSString *fullMessage = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    if (!fullMessage)
    {
        return nil;
    }
    
    NSString *jsonPayload = [fullMessage substringFromIndex:[MessagePrefix length]];
    jsonPayload = [jsonPayload stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
    jsonPayload = [jsonPayload stringByTrimmingCharactersInSet:[NSCharacterSet controlCharacterSet]];
    
    NSDictionary *json = [NSJSONSerialization
        JSONObjectWithData:[jsonPayload dataUsingEncoding:NSUTF8StringEncoding]
        options:0
        error:error];
    
    return json;
}

@end
