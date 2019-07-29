#import "VFSAboutWindowController.h"
#import "VFSAppDelegate.h"
#import "VFSMessageListener.h"
#import "VFSNotificationDisplay.h"
#import "VFSForGitNotification.h"
#import "VFSProductInfoFetcher.h"
#import "VFSStatusBarItem.h"
#import "VFSNotificationDisplay.h"

@interface VFSAppDelegate ()

@property (weak) IBOutlet NSWindow *Window;
@property (strong) VFSStatusBarItem *StatusDisplay;
@property (strong) VFSMessageListener *messageListener;

- (void)displayNotification:(NSDictionary *_Nonnull)messageInfo;

@end

@implementation VFSAppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification
{
    self.messageListener = [[VFSMessageListener alloc]
        initWithSocket:NSTemporaryDirectory()
        callback:^(NSDictionary *messageInfo)
        {
            [self displayNotification:messageInfo];
        }];
    
    [self.messageListener startListening];
    
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
    [self.messageListener stopListening];
}

- (void)displayNotification:(NSDictionary *_Nonnull)messageInfo
{
    NSParameterAssert(messageInfo);
    
    VFSForGitNotification *notification;
    NSError *error;
    if (![VFSForGitNotification tryValidateMessage:messageInfo
                                 buildNotification:&notification
                                             error:&error])
    {
        NSLog(@"ERROR: Could not display notification. %@", [error description]);
        return;
    }
    
    VFSNotificationDisplay *notificationDisplay =
    [[VFSNotificationDisplay alloc] initWithTitle:notification.title
                                          message:notification.message];
    
    [notificationDisplay display];
}
@end
