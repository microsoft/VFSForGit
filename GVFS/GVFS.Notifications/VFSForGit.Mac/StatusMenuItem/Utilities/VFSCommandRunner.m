#import "VFSCommandRunner.h"

NSString * const LaunchTerminalScriptFormat = @"\
tell application \"Terminal\" \n\
activate \n\
do script (\"echo;echo Running gvfs command: %@;echo You might need to type Admin password;%@ \") \n\
end tell";

@implementation VFSCommandRunner

- (void) runCommand:(NSString *) command
{
    NSString *scriptSource = [NSString stringWithFormat:LaunchTerminalScriptFormat, command, command];
    NSAppleScript *scriptObject = [[NSAppleScript alloc] initWithSource:scriptSource];
    NSDictionary *errorDict;
    NSAppleEventDescriptor *returnDescriptor = NULL;
    
    returnDescriptor = [scriptObject executeAndReturnError: &errorDict];
    if (returnDescriptor == nil)
    {
        NSLog(@"Error running %@. %@", command, [errorDict description]);
    }
}

@end
