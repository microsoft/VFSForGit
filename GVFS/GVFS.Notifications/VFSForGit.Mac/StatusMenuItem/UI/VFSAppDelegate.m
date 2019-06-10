#import "VFSAboutWindowController.h"
#import "VFSAppDelegate.h"
#import "VFSProductInfoFetcher.h"
#import "VFSStatusBarItem.h"
#import "VFSUserNotification.h"

@interface VFSAppDelegate ()

@property (weak) IBOutlet NSWindow *Window;
@property (strong) VFSStatusBarItem *StatusDisplay;

@end

@implementation VFSAppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification
{
    // TODO : Create a message listener object and
    // start listening for incoming notification
    // requests.
    
    VFSProductInfoFetcher *productInfoFetcher =
    [[VFSProductInfoFetcher alloc]
     initWithProcessRunner:[[VFSProcessRunner alloc] initWithProcessFactory:^NSTask *
    {
        return [[NSTask alloc] init];
    }]];
    
    self.StatusDisplay = [[VFSStatusBarItem alloc] initWithAboutWindowController:
                          [[VFSAboutWindowController alloc]
                           initWithProductInfoFetcher:productInfoFetcher]];
    
    [self.StatusDisplay load];
}

- (void)applicationWillTerminate:(NSNotification *)aNotification
{
    // TODO : Cleanup IPC objects.
}

@end
