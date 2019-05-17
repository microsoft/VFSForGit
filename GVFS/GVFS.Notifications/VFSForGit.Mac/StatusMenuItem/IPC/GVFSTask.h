#import <Foundation/Foundation.h>

@interface GVFSTask : NSObject

@property (assign) int ExitCode;
@property (strong) NSError *Error;

- (NSString *) GitVersion;
- (NSString *) VFSForGitVersion;

@end
