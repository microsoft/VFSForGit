#import "AboutWindowController.h"
#import "AppDelegate.h"
#import "UserNotification.h"
#import "StatusBarItem.h"

@interface AppDelegate ()

@property (weak) IBOutlet NSWindow *Window;
@property (strong) StatusBarItem *StatusDisplay;

@end

// Placeholder class to handle app launch and terminate
// callbacks
@implementation AppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification
{
    // TODO : Create a message listener object and
    // start listening for incoming notification
    // requests.
    
    self.StatusDisplay = [[StatusBarItem alloc] init];
    [self.StatusDisplay Load];
}


- (void)applicationWillTerminate:(NSNotification *)aNotification
{
    // TODO : Cleanup IPC objects.
}

@end
