#import "VFSAboutWindowController.h"
#import "VFSStatusBarItem.h"
#import "VFSProductInfoFetcher.h"

@interface VFSStatusBarItem ()

@property (strong, nonnull) NSStatusItem *statusItem;
@property (strong, nonnull) VFSAboutWindowController *aboutWindowController;

@end

@implementation VFSStatusBarItem

- (instancetype)initWithAboutWindowController:(VFSAboutWindowController *)aboutWindowController
{
    if (aboutWindowController == nil)
    {
        self = nil;
    }
    else if (self = [super init])
    {
        _aboutWindowController = aboutWindowController;
    }
    
    return self;
}

- (void)load
{
    self.statusItem = [[NSStatusBar systemStatusBar]
                       statusItemWithLength:NSVariableStatusItemLength];
    
    [self.statusItem setHighlightMode:YES];
    
    [self addStatusButton];
    [self addMenuItems];
}

- (IBAction)handleMenuClick:(id)sender
{
    switch (((NSButton *) sender).tag)
    {
        case 0:
        {
            [self displayAboutBox];
            break;
        }
        
        default:
        {
            break;
        }
    }
}

- (IBAction)displayAboutBox
{
    [[NSApplication sharedApplication] activateIgnoringOtherApps:YES];
    [self.aboutWindowController showWindow:self];
    [self.aboutWindowController.window makeKeyAndOrderFront:self];
}

- (NSMenu *)getStatusMenu
{
    return self.statusItem.menu;
}

- (void)addStatusButton
{
    NSImage *image = [NSImage imageNamed:@"StatusItem"];
    
    [image setTemplate:YES];
    
    [self.statusItem.button setImage:image];
    [self.statusItem.button setTarget:nil];
    [self.statusItem.button setAction:nil];
}

- (void)addMenuItems
{
    NSUInteger index = 0;
    NSMenu *menu = [[NSMenu alloc] init];
    NSMenuItem *aboutItem = [[NSMenuItem alloc]
                             initWithTitle:@"About VFS For Git"
                             action:@selector(handleMenuClick:)
                             keyEquivalent:@""];
    
    [aboutItem setTag:0];
    [aboutItem setTarget:self];
    [menu insertItem:[NSMenuItem separatorItem] atIndex:index++];
    [menu insertItem:aboutItem atIndex:index++];
    [menu setAutoenablesItems:NO];
    
    [self.statusItem setMenu:menu];
}

@end
