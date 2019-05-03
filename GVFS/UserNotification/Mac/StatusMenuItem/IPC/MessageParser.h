//
//  MessageParser.h
//  VFSForGit
//
//  Created by Ameen on 5/3/19.
//  Copyright © 2019 Microsoft. All rights reserved.
//

#import <Foundation/Foundation.h>

@interface MessageParser : NSObject

- (NSDictionary *) Parse:(NSData *) data error:(NSError **) error;

@end
