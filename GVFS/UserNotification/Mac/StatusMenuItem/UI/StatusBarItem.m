#import "AboutWindowController.h"
#import "StatusBarItem.h"
#import "GVFSTask.h"

@interface StatusBarItem ()

@property (strong) NSStatusItem *StatusItem;

@end

@implementation StatusBarItem

- (void) Load
{
    self.StatusItem = [[NSStatusBar systemStatusBar] statusItemWithLength:NSVariableStatusItemLength];
    
    [self.StatusItem setHighlightMode:YES];
    
    [self AddStatusButton];
    [self AddMenuItems];
}

- (IBAction) HandleStatusItemClick:(id) sender
{
    NSLog(@"Button clicked");
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
        
        case 1:
        {
            NSLog(@"No action for Repositories");
            break;
        }
    }
}

- (IBAction) DisplayAboutBox
{
    AboutWindowController *aboutWindowController = [[AboutWindowController alloc] initWithWindowNibName:@"AboutWindowController"];
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
    
    NSMenuItem *repositoriesItem = [[NSMenuItem alloc] initWithTitle:@"VFSForGit Repositories" action:@selector(HandleMenuClick:) keyEquivalent:@""];
    [repositoriesItem setTag:1];
    [repositoriesItem setTarget:self];
    [menu insertItem:repositoriesItem atIndex:index++];
    
    GVFSTask *gvfsTask = [[GVFSTask alloc] init];
    NSArray *mountedRepos = [gvfsTask MountedRepositories];
    for (NSString *repo in mountedRepos)
    {
        NSMutableAttributedString *title = [[NSMutableAttributedString alloc] initWithString:repo];
        [title setAttributes:@{NSFontAttributeName : [NSFont menuFontOfSize:12]} range:NSMakeRange(0, title.length)];
    
        NSMenuItem *repoItem = [[NSMenuItem alloc] init];
        repoItem.attributedTitle = title;
        repoItem.indentationLevel = 1;
        [menu insertItem:repoItem atIndex:index++];
    }
    
    NSMenuItem *aboutItem = [[NSMenuItem alloc] initWithTitle:@"About VFSForGit" action:@selector(HandleMenuClick:) keyEquivalent:@""];
    [aboutItem setTag:0];
    [aboutItem setTarget:self];
    [menu insertItem:[NSMenuItem separatorItem] atIndex:index++];
    [menu insertItem:aboutItem atIndex:index++];
    
    [menu setAutoenablesItems:NO];
    [self.StatusItem setMenu:menu];
}

@end
