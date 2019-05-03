#import "AboutWindowController.h"
#import "AppDelegate.h"
#import "MessageListener.h"
#import "UserNotification.h"
#import "StatusBarItem.h"

@interface AppDelegate ()

@property (weak) IBOutlet NSWindow *Window;
@property (strong) MessageListener *MessageListener;
@property (strong) StatusBarItem *StatusDisplay;

@end

@implementation AppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification
{
    self.MessageListener = [[MessageListener alloc]
        initWithSocket:NSTemporaryDirectory()
        callback:^(NSDictionary *messageInfo)
        {
            @autoreleasepool
            {
                UserNotification *notification = [[UserNotification alloc] initWithInfo:messageInfo];
                [notification display];
            }
        }];
    
    [self.MessageListener StartListening];    
    
    self.StatusDisplay = [[StatusBarItem alloc] init];
    [self.StatusDisplay Load];
}


- (void)applicationWillTerminate:(NSNotification *)aNotification
{
    [self.MessageListener StopListening];
}

@end
