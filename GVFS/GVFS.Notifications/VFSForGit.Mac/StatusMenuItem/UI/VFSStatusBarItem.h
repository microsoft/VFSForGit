#import <Cocoa/Cocoa.h>

@interface VFSStatusBarItem : NSObject

- (instancetype _Nullable)initWithAboutWindowController:(VFSAboutWindowController *_Nonnull)aboutWindowController;
- (void)load;
- (NSMenu *_Nullable)getStatusMenu;
- (IBAction)handleMenuClick:(id)sender;

@end
