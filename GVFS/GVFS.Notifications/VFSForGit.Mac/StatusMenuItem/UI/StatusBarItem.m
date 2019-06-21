#import "AboutWindowController.h"
#import "StatusBarItem.h"
#import "GVFSTask.h"

@interface StatusBarItem ()

@property (strong) NSStatusItem *StatusItem;

@end

@implementation StatusBarItem

- (void) Load
{
    self.StatusItem = [[NSStatusBar systemStatusBar]
        statusItemWithLength:NSVariableStatusItemLength];
    
    [self.StatusItem setHighlightMode:YES];
    
    [self AddStatusButton];
    [self AddMenuItems];
}

// Placeholder method that gets called back when
// user clicks on StatusItem
- (IBAction) HandleStatusItemClick:(id) sender
{
    // TODO : Decide on what action to perform
    // on button click. Also, this action may
    // not be needed since StatusItem
    // automatically displays drop down menu
    // on button click.
}

- (IBAction) HandleMenuClick:(id) sender
{
    switch (((NSButton *) sender).tag)
    {
        case 0:
        {
            [self DisplayAboutBox];
            break;
        }
        
        default:
        {
            break;
        }
    }
}

- (IBAction) DisplayAboutBox
{
    AboutWindowController *aboutWindowController = [[AboutWindowController alloc]
        initWithWindowNibName:@"AboutWindowController"];
    [aboutWindowController showWindow:self];
    [aboutWindowController.window makeKeyAndOrderFront:self];
}

- (void) AddStatusButton
{
    NSImage *image = [NSImage imageNamed:@"StatusItem"];
    
    [image setTemplate:YES];
    
    [self.StatusItem.button setImage:image];
    [self.StatusItem.button setTarget:self];
    [self.StatusItem.button setAction:@selector(HandleStatusItemClick:)];
}

- (void) AddMenuItems
{
    NSUInteger index = 0;
    NSMenu *menu = [[NSMenu alloc] init];
    NSMenuItem *aboutItem = [[NSMenuItem alloc]
        initWithTitle:@"About VFS For Git"
        action:@selector(HandleMenuClick:)
        keyEquivalent:@""];
    
    [aboutItem setTag:0];
    [aboutItem setTarget:self];
    [menu insertItem:[NSMenuItem separatorItem] atIndex:index++];
    [menu insertItem:aboutItem atIndex:index++];
    [menu setAutoenablesItems:NO];
    
    [self.StatusItem setMenu:menu];
}

@end
