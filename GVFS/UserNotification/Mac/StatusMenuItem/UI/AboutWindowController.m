#import "GVFSTask.h"
#import "AboutWindowController.h"

@interface AboutWindowController ()

@end

@implementation AboutWindowController

- (void)windowWillLoad
{
    GVFSTask *gvfsTask = [[GVFSTask alloc] init];
    
    self.VFSForGitVersion = [gvfsTask VFSForGitVersion];
    self.GitVersion = [gvfsTask GitVersion];
    
    [super windowWillLoad];
}

@end
