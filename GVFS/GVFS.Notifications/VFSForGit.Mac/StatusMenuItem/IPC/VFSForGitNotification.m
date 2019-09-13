#import "VFSForGitNotification.h"
#import "VFSNotificationErrors.h"

NSString * const IdentifierKey = @"Id";
NSString * const EnlistmentKey = @"Enlistment";
NSString * const EnlistmentCountKey = @"EnlistmentCount";
NSString * const NewUpgradeVersionKey = @"NewVersion";
NSString * const TitleKey = @"Title";
NSString * const ActionTitleKey = @"ActionTitle";
NSString * const IsActionableKey = @"IsActionable";
NSString * const MessageKey = @"Message";
NSString * const GVFSCommandKey = @"GVFSCommand";

NSString * const AutomountTitle = @"GVFS AutoMount";
NSString * const MountGVFSCommandFormat = @"gvfs mount %@";
NSString * const MountActionTitle = @"Retry";
NSString * const AutomountStartMessageFormat = @"Attempting to mount %lu GVFS repos(s)";
NSString * const AutomountSuccessMessageFormat = @"The following GVFS repo is now mounted: \n%@";
NSString * const AutomountFailureMessageFormat = @"The following GVFS repo failed to mount: \n%@";

NSString * const UpgradeAvailableTitleFormat = @"New version %@ is available";
NSString * const UpgradeAvailableMessage = @"Upgrade will unmount and remount gvfs repos, ensure you are at a stopping point.\nWhen ready, click Upgrade button to run upgrade.";
NSString * const UpgradeActionTitle = @"Upgrade";
NSString * const UpgradeGVFSCommandFormat = @"sudo gvfs upgrade --confirm";

@interface VFSForGitNotification()

@property (readwrite) Identifier identifier;
@property (readwrite) NSString *title;
@property (readwrite) NSString *message;
@property (readwrite) NSString *actionTitle;
@property (readwrite) NSString *gvfsCommand;
@property (readwrite) BOOL actionable;

NS_ASSUME_NONNULL_BEGIN

- (instancetype _Nullable)initAsMountSuccessWithMessage:(NSDictionary *)messageDict
                                                  error:(NSError *__autoreleasing *)error;

- (instancetype _Nullable)initAsMountFailureWithMessage:(NSDictionary *)messageDict
                                                  error:(NSError *__autoreleasing *)error;

- (instancetype _Nullable)initAsMountWithMessage:(NSDictionary *)messageDict
                                           title:(NSString *)title
                                   messageFormat:(NSString *)messageFormat
                                    actionFormat:(NSString * _Nullable)commandFormat
                                           error:(NSError *__autoreleasing *)error;

- (instancetype)initAsUpgradeAvailableWithMessage:(NSDictionary *)messageDict
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
        
        case UpgradeAvailable:
        {
            *notification = [[VFSForGitNotification alloc]
                             initAsUpgradeAvailableWithMessage:jsonMessage
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
        if ((repoCount != nil) && [repoCount isKindOfClass:[NSNumber class]])
        {
            _title = [AutomountTitle copy];
            _message = [[NSString stringWithFormat:AutomountStartMessageFormat, [repoCount unsignedIntegerValue]] copy];
        }
        else
        {
            if (error != nil)
            {
                *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                             code:VFSForGitMissingRepoCountError
                                         userInfo:@{ NSLocalizedDescriptionKey : @"Missing repos count in AutomountStart message" }];
            }
            
            self = nil;
        }
    }
    
    return self;
}

- (instancetype)initAsMountSuccessWithMessage:(NSDictionary *)messageDict
                                        error:(NSError *__autoreleasing *)error
{
    return self = [self initAsMountWithMessage:messageDict
                                         title:(NSString *)AutomountTitle
                                 messageFormat:(NSString *)AutomountSuccessMessageFormat
                                  actionFormat:nil
                                         error:error];
}

- (instancetype)initAsMountFailureWithMessage:(NSDictionary *)messageDict
                                        error:(NSError *__autoreleasing *)error
{
    return self = [self initAsMountWithMessage:messageDict
                                         title:(NSString *)AutomountTitle
                                 messageFormat:(NSString *)AutomountFailureMessageFormat
                                  actionFormat:MountGVFSCommandFormat
                                         error:error];
}

- (instancetype)initAsMountWithMessage:(NSDictionary *)messageDict
                                 title:(NSString *)title
                         messageFormat:(NSString *)messageFormat
                          actionFormat:(NSString *)commandFormat
                                 error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(title);
    NSParameterAssert(messageFormat);
    
    if (self = [super init])
    {
        id enlistment = messageDict[EnlistmentKey];
        if ((enlistment != nil) && [enlistment isKindOfClass:[NSString class]])
        {
            _title = [title copy];
            _message = [[NSString stringWithFormat:
                         (NSString *)messageFormat,
                         enlistment] copy];
            if (commandFormat != nil)
            {
                _gvfsCommand = [[NSString stringWithFormat:commandFormat, enlistment] copy];
                _actionable = YES;
                _actionTitle = MountActionTitle;
            }
            
            return self;
        }
        else
        {
            if (error != nil)
            {
                *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                             code:VFSForGitMissingEntitlementInfoError
                                         userInfo:@{ NSLocalizedDescriptionKey : @"ERROR: missing enlistment info." }];
            }
            
            self = nil;
        }
    }
    
    return self;
}

- (instancetype)initAsUpgradeAvailableWithMessage:(NSDictionary *)messageDict
                                            error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(messageDict);
    
    if (self = [super init])
    {
        id newVersion = messageDict[NewUpgradeVersionKey];
        if ((newVersion != nil) && [newVersion isKindOfClass:[NSString class]])
        {
            _title = [[NSString stringWithFormat:UpgradeAvailableTitleFormat, newVersion] copy];
            _message = [UpgradeAvailableMessage copy];
            _actionTitle = [UpgradeActionTitle copy];
            _actionable = YES;
            _gvfsCommand = [UpgradeGVFSCommandFormat copy];
        }
        else
        {
            if (error != nil)
            {
                *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                             code:VFSForGitMissingEntitlementInfoError
                                         userInfo:@{ NSLocalizedDescriptionKey : @"ERROR: missing new upgrade version info." }];
            }
            
            self = nil;
        }
    }
    
    return self;
}

- (void)encodeWithCoder:(NSCoder *)aCoder
{
    [aCoder encodeObject:[NSNumber numberWithInt:[self identifier]] forKey:IdentifierKey];
    [aCoder encodeObject:[self title] forKey:TitleKey];
    [aCoder encodeObject:[self message] forKey:MessageKey];
    [aCoder encodeObject:[self actionTitle] forKey:ActionTitleKey];
    [aCoder encodeObject:[self gvfsCommand] forKey:GVFSCommandKey];
    [aCoder encodeObject:[NSNumber numberWithBool:[self actionable]] forKey:IsActionableKey];
}

- (instancetype)initWithCoder:(NSCoder *)aDecoder
{
    if (self = [super init])
    {
        _identifier = [[aDecoder decodeObjectForKey:IdentifierKey] integerValue];
        _title = [[aDecoder decodeObjectForKey:TitleKey] copy];
        _message = [[aDecoder decodeObjectForKey:MessageKey] copy];
        _actionTitle = [[aDecoder decodeObjectForKey:ActionTitleKey] copy];
        _gvfsCommand = [[aDecoder decodeObjectForKey:GVFSCommandKey] copy];
        _actionable = [[aDecoder decodeObjectForKey:IsActionableKey] boolValue];
    }
    
    return self;
}

@end
