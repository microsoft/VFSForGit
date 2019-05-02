#import <Cocoa/Cocoa.h>
#import "NotificationRequestHandler.h"

@implementation NotificationRequestHandler

- (void) notificationHandler:(NSNotification *) notification
{
    NSAlert *alert = [[NSAlert alloc] init];
    [alert setMessageText:[[notification userInfo] objectForKey:@"message"]];
    [alert setInformativeText:[[notification userInfo] objectForKey:@"title"]];
    [alert runModal];
}

@end
