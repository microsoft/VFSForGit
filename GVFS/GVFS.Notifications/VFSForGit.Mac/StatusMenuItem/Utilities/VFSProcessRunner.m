#import "VFSProcessRunner.h"
#import "VFSNotificationErrors.h"

@interface VFSProcessRunner()

@property (strong) ProcessFactory processFactory;

@end

@implementation VFSProcessRunner

- (instancetype)initWithProcessFactory:(ProcessFactory)processFactory
{
    if (processFactory == nil)
    {
        self = nil;
    }
    else if (self = [super init])
    {
        _processFactory = processFactory;
    }
    
    return self;
}

/**
 Runs an executable specified by path and args. If the executable could be run
 successfully - output will contain the executable's combined stderr/stdout
 output and the method returns YES. In case of failure, it returns NO and error
 will hold the executable's combined stderr/stdout output.

 @param path - specify full path to the executable.
 @param args - specify any command line args to pass to the executable.
 @param output - contains executable's output, if it was successfully run.
 @param error - contains executable's output, if it exited with an error.
 @return YES if the executable was successfully run, NO otherwise.
 */
- (BOOL)tryRunExecutable:(NSURL *)path
                    args:(NSArray<NSString *> *)args
                  output:(NSString *__autoreleasing *)output
                   error:(NSError *__autoreleasing *)error
{
    NSParameterAssert(path);
    NSParameterAssert(output);
    
    NSTask *task = self.processFactory();
    NSPipe *taskOut = [NSPipe pipe];
    
    task.executableURL = path;
    task.arguments = args;
    task.standardOutput = taskOut;
    task.standardError = taskOut;
    
    int exitCode = -1;
    
    if ([task launchAndReturnError:error])
    {
        [task waitUntilExit];
        
        exitCode = [task terminationStatus];
        
        *output = [[NSString alloc] initWithData:[taskOut.fileHandleForReading availableData]
                                        encoding:NSUTF8StringEncoding];
        
        if (0 != exitCode)
        {
            if (error != nil)
            {
                NSDictionary *userInfo = @{ NSLocalizedDescriptionKey : *output };
                *error = [NSError errorWithDomain:VFSForGitNotificationErrorDomain
                                             code:exitCode
                                         userInfo:userInfo];
            }
            
            *output = nil;
        }
    }
    
    return 0 == exitCode;
}

@end
