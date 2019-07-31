#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface VFSMessageParser : NSObject

+ (BOOL)tryParseData:(NSData *)data
             message:(NSDictionary *_Nullable __autoreleasing *_Nonnull)parsedMessage
               error:(NSError *__autoreleasing *)error;

@end

NS_ASSUME_NONNULL_END
