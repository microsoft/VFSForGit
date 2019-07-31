#import <Cocoa/Cocoa.h>
#import "VFSProductInfoFetcher.h"

@interface VFSAboutWindowController : NSWindowController

@property (readonly, nullable) NSString *vfsforgitVersion;
@property (readonly, nullable) NSString *gitVersion;

- (instancetype _Nullable)initWithProductInfoFetcher:(VFSProductInfoFetcher *_Nonnull)productInfoFetcher;

@end
