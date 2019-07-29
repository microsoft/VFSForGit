#import <Foundation/Foundation.h>
#import "VFSMockProductInfoFetcher.h"

@interface VFSMockProductInfoFetcher()

@property (copy) NSString *gitVersion;
@property (copy) NSString *vfsforgitVersion;

@end

@implementation VFSMockProductInfoFetcher

- (instancetype) initWithGitVersion:(NSString *) gitVersion
                   vfsforgitVersion:(NSString *) vfsforgitVersion
{
    if (self = [super init])
    {
        _gitVersion = [gitVersion copy];
        _vfsforgitVersion = [vfsforgitVersion copy];
    }
    
    return self;
}

- (BOOL) tryGetVFSForGitVersion:(NSString *__autoreleasing *) version
                          error:(NSError *__autoreleasing *) error
{
    *version = self.vfsforgitVersion;
    return YES;
}

- (BOOL) tryGetGitVersion:(NSString *__autoreleasing *) version
                    error:(NSError *__autoreleasing *) error
{
    *version = self.gitVersion;
    return YES;
}

@end
