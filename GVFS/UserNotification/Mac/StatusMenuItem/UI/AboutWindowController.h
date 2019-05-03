//
//  AboutWindowController.h
//  VFSForGit
//
//  Created by Ameen on 5/7/19.
//  Copyright © 2019 Microsoft. All rights reserved.
//

#import <Cocoa/Cocoa.h>

@interface AboutWindowController : NSWindowController

@property (copy) NSString *VFSForGitVersion;
@property (copy) NSString *GitVersion;

@end
