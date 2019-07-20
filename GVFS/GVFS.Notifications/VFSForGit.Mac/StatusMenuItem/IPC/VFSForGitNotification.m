#import "VFSForGitNotification.h"
#import "VFSNotificationErrors.h"

NSString * const IdentifierKey = @"Id";
NSString * const EnlistmentKey = @"Enlistment";
NSString * const EnlistmentCountKey = @"EnlistmentCount";
NSString * const TitleKey = @"Title";
NSString * const MessageKey = @"Message";

NSString * const AutomountTitle = @"GVFS AutoMount";
NSString * const AutomountStartMessageFormat = @"Attempting to mount %lu GVFS repos(s)";
NSString * const AutomountSuccessMessageFormat = @"The following GVFS repo is now mounted: \n%@";
NSString * const AutomountFailureMessageFormat = @"The following GVFS repo failed to mount: \n%@";

@interface VFSForGitNotification()

@property (readwrite) NSString *title;
@property (readwrite) NSString *message;

NS_ASSUME_NONNULL_BEGIN

- (instancetype _Nullable)initAsMountSuccessWithMessage:(NSDictionary *)messageDict
                                                  error:(NSError *__autoreleasing *)error;

- (instancetype _Nullable)initAsMountFailureWithMessage:(NSDictionary *)messageDict
                                                  error:(NSError *__autoreleasing *)error;

- (instancetype _Nullable)initAsMountWithMessage:(NSDictionary *)messageDict
                                           title:(NSString *)title
                                   messageFormat:(NSString *)messageFormat
                                           error:(NSError *__autoreleasing *)error;

NS_ASSUME_NONNULL_END

@end

@implementation VFSForGitNotification

+ (BOOL)tryValidateMessage:(NSDictionary *)jsonMessage
         buildNotification:(VFSForGitNotification **)notification
                     error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(notification);
    NSParameterAssert(jsonMessage);
    
    id identifier = jsonMessage[IdentifierKey];
    if (![identifier isKindOfClass:[NSNumber class]])
    {
        if (error != nil)
        {
            *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                         code:VFSForGitInvalidMessageIdFormatError
                                     userInfo:@{ NSLocalizedDescriptionKey : @"Unexpected message id/format)" }];
        }
        
        return NO;
    }
    
    Identifier idValue = [identifier integerValue];
    NSError *initError = nil;
    switch (idValue)
    {
        case AutomountStart:
        {
            *notification = [[VFSForGitNotification alloc]
                             initAsAutomountStartWithMessage:jsonMessage
                             error:&initError];
            break;
        }
        
        case MountSuccess:
        {
            *notification = [[VFSForGitNotification alloc]
                             initAsMountSuccessWithMessage:jsonMessage
                             error:&initError];
            break;
        }
            
        case MountFailure:
        {
            *notification = [[VFSForGitNotification alloc]
                             initAsMountFailureWithMessage:jsonMessage
                             error:&initError];
            break;
        }
            
        default:
        {
            *notification = nil;
            initError = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                            code:VFSForGitUnsupportedMessageError
                                        userInfo:@{ NSLocalizedDescriptionKey : @"Unrecognised message id" }];
            break;
        }
    }
    
    if (error != nil)
    {
        *error = initError;
    }
    
    return *notification != nil;
}

#pragma mark Private initializers

- (instancetype)initAsAutomountStartWithMessage:(NSDictionary *)messageDict
                                          error:(NSError *__autoreleasing *)error
{
    if (self = [super init])
    {
        id repoCount = messageDict[EnlistmentCountKey];
        if (repoCount && [repoCount isKindOfClass:[NSNumber class]])
        {
            _title = [AutomountTitle copy];
            _message = [[NSString stringWithFormat:AutomountStartMessageFormat, [repoCount unsignedIntegerValue]] copy];
            return self;
        }
        
        if (error != nil)
        {
            *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                         code:VFSForGitMissingRepoCountError
                                     userInfo:@{ NSLocalizedDescriptionKey : @"Missing repos count in AutomountStart message" }];
        }
        
        self = nil;
    }
    
    return self;
}

- (instancetype)initAsMountSuccessWithMessage:(NSDictionary *)messageDict
                                        error:(NSError *__autoreleasing *)error
{
    return self = [self initAsMountWithMessage:messageDict
                                         title:(NSString *)AutomountTitle
                                 messageFormat:(NSString *)AutomountSuccessMessageFormat
                                         error:error];
}

- (instancetype)initAsMountFailureWithMessage:(NSDictionary *)messageDict
                                        error:(NSError *__autoreleasing *)error
{
    return self = [self initAsMountWithMessage:messageDict
                                         title:(NSString *)AutomountTitle
                                 messageFormat:(NSString *)AutomountFailureMessageFormat
                                         error:error];
}

- (instancetype)initAsMountWithMessage:(NSDictionary *)messageDict
                                 title:(NSString *)title
                         messageFormat:(NSString *)messageFormat
                                 error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(title);
    NSParameterAssert(messageFormat);
    
    if (self = [super init])
    {
        id enlistment = messageDict[EnlistmentKey];
        if (enlistment && [enlistment isKindOfClass:[NSString class]])
        {
            _title = [title copy];
            _message = [[NSString stringWithFormat:
                         (NSString *)messageFormat,
                         enlistment] copy];
            return self;
        }
        
        if (error != nil)
        {
            *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                         code:VFSForGitMissingEntitlementInfoError
                                     userInfo:@{ NSLocalizedDescriptionKey : @"ERROR: missing enlistment info." }];
        }
        
        self = nil;
    }
    
    return self;
}

@end
