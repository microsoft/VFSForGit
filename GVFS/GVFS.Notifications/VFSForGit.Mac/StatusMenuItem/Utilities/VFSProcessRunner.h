#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

typedef NSTask *_Nonnull (^ProcessFactory)(void);

@interface VFSProcessRunner : NSObject

- (instancetype _Nullable)initWithProcessFactory:(ProcessFactory)processFactory;
- (BOOL)tryRunExecutable:(NSURL *)path
                    args:(NSArray<NSString *> *_Nullable)args
                  output:(NSString *_Nullable __autoreleasing *_Nonnull)output
                   error:(NSError * __autoreleasing *)error;

@end

NS_ASSUME_NONNULL_END
