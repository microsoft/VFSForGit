//
//  GVFSTask.h
//  VFSForGit
//
//  Created by Ameen on 5/6/19.
//  Copyright © 2019 Microsoft. All rights reserved.
//

#import <Foundation/Foundation.h>

@interface GVFSTask : NSObject

@property (assign) int ExitCode;
@property (strong) NSError *Error;

- (NSArray *) MountedRepositories;
- (NSString *) GitVersion;
- (NSString *) VFSForGitVersion;

@end
