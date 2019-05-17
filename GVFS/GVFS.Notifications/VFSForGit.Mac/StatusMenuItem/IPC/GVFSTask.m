#import "GVFSTask.h"

@interface GVFSTask()

@property (strong) NSConditionLock *TaskLock;
@property (strong) NSTask *Task;

@end

// Placeholder class to get VFSForGit info that
// gets displayed in the Status Item.
@implementation GVFSTask

- (NSString *) VFSForGitVersion
{
    // TODO : Get actual VFSForGit version
    return @"";
}

- (NSString *) GitVersion
{
    // TODO : Get actual git version
    return @"";
}

@end
