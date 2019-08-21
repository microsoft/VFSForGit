#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

extern NSString * const KnownMessagePrefix;

typedef NS_ENUM(NSInteger, Identifier)
{
    AutomountStart,
    MountSuccess,
    MountFailure,
    UpgradeAvailable,
    UnknownMessage
};

@interface VFSForGitNotification : NSObject <NSCoding>

@property (assign, readonly) Identifier identifier;
@property (copy, readonly) NSString *title;
@property (copy, readonly) NSString *actionTitle;
@property (copy, readonly) NSString *message;
@property (copy, readonly) NSString *gvfsCommand;
@property (assign, readonly) BOOL actionable;

+ (BOOL)tryValidateMessage:(NSDictionary *)jsonMessage
         buildNotification:(VFSForGitNotification *_Nullable *_Nonnull)notification
                     error:(NSError *__autoreleasing *)error;
@end

NS_ASSUME_NONNULL_END
