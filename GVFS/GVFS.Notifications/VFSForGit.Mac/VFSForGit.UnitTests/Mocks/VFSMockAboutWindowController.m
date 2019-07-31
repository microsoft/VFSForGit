#import <Foundation/Foundation.h>
#import "VFSMockAboutWindowController.h"

@interface VFSMockAboutWindowController()

@property (readwrite) BOOL aboutBoxDisplayed;

@end

@implementation VFSMockAboutWindowController

- (instancetype) initWithProductInfo:(VFSProductInfoFetcher *) productInfo
{
    self = [super initWithProductInfoFetcher:productInfo];
    return self;
}

- (IBAction)showWindow:(nullable id)sender
{
    self.aboutBoxDisplayed = YES;
}

@end
