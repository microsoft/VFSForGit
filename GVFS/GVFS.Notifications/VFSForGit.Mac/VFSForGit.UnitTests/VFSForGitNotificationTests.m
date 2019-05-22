#import <XCTest/XCTest.h>
#import "VFSForGitNotification.h"

@interface VFSForGitNotificationTests : XCTestCase
@end

@implementation VFSForGitNotificationTests

- (void)testCreateNotificationWithMissingIdFails
{
    NSDictionary *message = @{
                              @"Title" : @"foo",
                              @"Message" : @"bar",
                              @"Enlistment" : @"/foo/bar",
                              @"EnlistmentCount" : [NSNumber numberWithLong:0]
                              };
    
    NSError *error;
    VFSForGitNotification *notification;
    
    XCTAssertFalse([VFSForGitNotification tryValidateMessage:message
                                           buildNotification:&notification
                                                       error:&error]);
    XCTAssertNotNil(error);
}

- (void)testCreateNotificationWithInvalidIdFails
{
    NSDictionary *message = @{
                              @"Id" : [NSNumber numberWithLong:32],
                              @"Title" : @"foo",
                              @"Message" : @"bar",
                              @"EnlistmentCount" : [NSNumber numberWithLong:0]
                              };
    
    NSError *error;
    VFSForGitNotification *notification;
    XCTAssertFalse([VFSForGitNotification tryValidateMessage:message
                                           buildNotification:&notification
                                                       error:&error]);
    XCTAssertNotNil(error);
}

- (void)testCreateAutomountNotificationWithValidMessageSucceeds
{
    NSDictionary *message = @{
                              @"Id" : [NSNumber numberWithLong:0],
                              @"EnlistmentCount" : [NSNumber numberWithLong:5]
                              };
    
    NSError *error;
    VFSForGitNotification *notification;
    XCTAssertTrue([VFSForGitNotification tryValidateMessage:message
                                          buildNotification:&notification
                                                      error:&error]);
    XCTAssertTrue([notification.title isEqualToString:@"GVFS AutoMount"]);
    XCTAssertTrue([notification.message isEqualToString:@"Attempting to mount 5 GVFS repos(s)"]);
    XCTAssertNil(error);
}

- (void)testCreateMountNotificationWithValidMessageSucceeds
{
    NSString *enlistment = @"/Users/foo/bar/foo.bar";
    NSDictionary *message = @{
                              @"Id" : [NSNumber numberWithLong:1],
                              @"Enlistment" : enlistment
                              };
    
    NSError *error;
    VFSForGitNotification *notification;
    XCTAssertTrue([VFSForGitNotification tryValidateMessage:message
                                          buildNotification:&notification
                                                      error:&error]);
    XCTAssertTrue([notification.title isEqualToString:@"GVFS AutoMount"]);
    XCTAssertTrue([notification.message containsString:enlistment]);
    XCTAssertNil(error);
}

- (void)testCreateMountNotificationWithMissingEnlistmentFails
{
    NSDictionary *message = @{
                              @"Id" : [NSNumber numberWithLong:1],
                              };
    
    NSError *error;
    VFSForGitNotification *notification;
    XCTAssertFalse([VFSForGitNotification tryValidateMessage:message
                                           buildNotification:&notification
                                                       error:&error]);
    XCTAssertNotNil(error);
}

@end
