#import "AppDelegate.h"
#import "NotificationRequestHandler.h"

@interface AppDelegate ()

@property (weak) IBOutlet NSWindow *window;
@property (strong) NotificationRequestHandler *requestHandler;

@end

@implementation AppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification {
    self.requestHandler = [[NotificationRequestHandler alloc] init];
    
    [[NSDistributedNotificationCenter defaultCenter] addObserver:self.requestHandler
                                                        selector:@selector(notificationHandler:)
                                                            name:@"VFSForGitPlatformNativeNotification"
                                                          object:nil];
}


- (void)applicationWillTerminate:(NSNotification *)aNotification {
    // Insert code here to tear down your application
}


@end
