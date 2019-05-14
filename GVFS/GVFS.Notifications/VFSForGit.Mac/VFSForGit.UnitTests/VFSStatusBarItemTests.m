#import <XCTest/XCTest.h>
#import "VFSMockAboutWindowController.h"
#import "VFSMockProductInfoFetcher.h"
#import "VFSStatusBarItem.h"

NSString * const ExpectedAboutMenuTitle = @"About VFS For Git";

@interface VFSStatusBarItemTests : XCTestCase

@property (strong) VFSStatusBarItem *statusbarItem;
@property (strong) VFSMockAboutWindowController *aboutWindowController;

@end

@implementation VFSStatusBarItemTests

- (void)setUp
{
    [super setUp];
    
    VFSMockProductInfoFetcher *mockProductInfoFetcher = [[VFSMockProductInfoFetcher alloc]
                                                         initWithGitVersion:@""
                                                         vfsforgitVersion:@""];
    
    self.aboutWindowController = [[VFSMockAboutWindowController alloc]
                                  initWithProductInfoFetcher:mockProductInfoFetcher];
    self.statusbarItem = [[VFSStatusBarItem alloc]
                          initWithAboutWindowController:self.aboutWindowController];
    
    [self.statusbarItem load];
}

- (void)tearDown
{
    [super tearDown];
}

- (void)testStatusItemContainsAboutMenu
{
    NSMenu *statusMenu = [self.statusbarItem getStatusMenu];
    XCTAssertNotNil(statusMenu, @"Status bar does not contain VFSForGit menu");
    
    NSMenuItem *menuItem = [statusMenu itemWithTitle:ExpectedAboutMenuTitle];
    XCTAssertNotNil(menuItem, @"Missing \"%@\" item in VFSForGit menu", ExpectedAboutMenuTitle);
}

- (void)testAboutMenuClickDisplaysAboutBox
{
    [self.statusbarItem handleMenuClick:nil];
    
    XCTAssertTrue(self.aboutWindowController.aboutBoxDisplayed,
                  @"Clicking on \"%@\" menu does not show About box",
                  ExpectedAboutMenuTitle);
}

@end
