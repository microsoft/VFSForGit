#import <Foundation/Foundation.h>
#import "VFSProcessRunner.h"

NS_ASSUME_NONNULL_BEGIN

@interface VFSProductInfoFetcher : NSObject

- (instancetype _Nullable)initWithProcessRunner:(VFSProcessRunner *)processRunner;
- (BOOL)tryGetGitVersion:(NSString *_Nullable __autoreleasing *_Nonnull)version
                   error:(NSError **)error;
- (BOOL)tryGetVFSForGitVersion:(NSString *_Nullable __autoreleasing *_Nonnull)version
                         error:(NSError **)error;

@end

NS_ASSUME_NONNULL_END
