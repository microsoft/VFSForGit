#import <Foundation/Foundation.h>
#import "VFSProductInfoFetcher.h"

NS_ASSUME_NONNULL_BEGIN

@interface VFSMockProductInfoFetcher : VFSProductInfoFetcher

- (instancetype _Nullable) initWithGitVersion:(NSString *) gitVersion
                             vfsforgitVersion:(NSString *) vfsforgitVersion;

@end

NS_ASSUME_NONNULL_END
