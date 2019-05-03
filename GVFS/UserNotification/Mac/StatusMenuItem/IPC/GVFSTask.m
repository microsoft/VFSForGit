//
//  GVFSTask.m
//  VFSForGit
//
//  Created by Ameen on 5/6/19.
//  Copyright © 2019 Microsoft. All rights reserved.
//

#import "GVFSTask.h"

@interface GVFSTask()

@property (strong) NSConditionLock *TaskLock;
@property (strong) NSTask *Task;

@end

@implementation GVFSTask

- (NSArray *) MountedRepositories
{
    return [NSArray arrayWithObjects:@"~/Work/Foo", @"~/Work/Bar", @"~/Work/FooBar", nil];
}

- (NSString *) VFSForGitVersion
{
    return @"1.0.19116.1";
}

- (NSString *) GitVersion
{
    return @"2.20.1.vfs.1.1.104.g2ab7360";
}

- (BOOL) TryExecuteWithArgs:(NSArray *) args
{
    NSError *error;
    self.Task = [NSTask
        launchedTaskWithExecutableURL:[NSURL fileURLWithPath:@"/usr/local/vfsforgit/gvfs"]
        arguments:args
        error:&error
        terminationHandler:^(NSTask * task)
        {
        }];
    
    return YES;
}

@end
