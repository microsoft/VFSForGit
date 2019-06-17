#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

extern NSErrorDomain const VFSForGitNotificationErrorDomain;
typedef NS_ERROR_ENUM(VFSForGitNotificationErrorDomain, VFSForGitNotificationErrorCode)
{
    VFSForGitInitError = 200,
    VFSForGitAllocError,
    VFSForGitInvalidMessageIdFormatError,
    VFSForGitUnsupportedMessageError,
    VFSForGitMissingEntitlementInfoError,
    VFSForGitMissingRepoCountError,
    VFSForGitMessageParseError,
    VFSForGitMessageReadError,
};

NS_ASSUME_NONNULL_END
