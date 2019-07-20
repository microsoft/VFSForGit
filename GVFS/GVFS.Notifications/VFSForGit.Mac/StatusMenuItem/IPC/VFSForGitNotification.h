#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

extern NSString * const KnownMessagePrefix;

typedef NS_ENUM(NSInteger, Identifier)
{
    AutomountStart,
    MountSuccess,
    MountFailure,
    UnknownMessage
};

@interface VFSForGitNotification : NSObject

@property (assign, readonly) Identifier identifier;
@property (copy, readonly) NSString *title;
@property (copy, readonly) NSString *message;

+ (BOOL)tryValidateMessage:(NSDictionary *)jsonMessage
         buildNotification:(VFSForGitNotification *_Nullable *_Nonnull)notification
                     error:(NSError *__autoreleasing *)error;
@end

NS_ASSUME_NONNULL_END
