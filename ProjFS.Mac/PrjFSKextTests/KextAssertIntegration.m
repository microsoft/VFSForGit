#import "KextAssertIntegration.h"
#include <pthread.h>

extern void Assert(const char* file, unsigned line, const char* expression);

static pthread_key_t s_currentTestCaseKey;

static PFSKextTestCase* CurrentTestCase()
{
    return (__bridge PFSKextTestCase*)pthread_getspecific(s_currentTestCaseKey);
}

@implementation PFSKextTestCase
{
    uint32_t expectedKextAssertionFailures;
}

+ (void) initialize
{
    pthread_key_create(&s_currentTestCaseKey, NULL);
}

- (void) setUp
{
    pthread_setspecific(s_currentTestCaseKey, (__bridge void*)self);
}

- (void) tearDown
{
    XCTAssertEqual(self->expectedKextAssertionFailures, 0, "Expected assertions apparently did not occur");
    self->expectedKextAssertionFailures = 0;
    pthread_setspecific(s_currentTestCaseKey, NULL);
}

- (void) setExpectedFailedKextAssertionCount:(uint32_t)failedAssertionCount
{
    self->expectedKextAssertionFailures = failedAssertionCount;
}

- (bool) isKextAssertionFailureExpected
{
    if (self->expectedKextAssertionFailures > 0)
    {
        --self->expectedKextAssertionFailures;
        return true;
    }
    else
    {
        return false;
    }
}

@end

// catches kext assert()
void Assert(const char* file, unsigned line, const char* expression)
{
    PFSKextTestCase* test = CurrentTestCase();
    bool expected = [test isKextAssertionFailureExpected];
    if (expected)
    {
        return;
    }

    NSString* expressionString = [NSString stringWithCString:expression encoding:NSUTF8StringEncoding];
    _XCTFailureHandler(test, YES, file, line, expressionString, @"");
}

// catches kext assertf() - currently no source file & line number support
void panic(const char* format, ...)
{
    PFSKextTestCase* test = CurrentTestCase();
    bool expected = [test isKextAssertionFailureExpected];
    if (expected)
    {
        return;
    }
    
    va_list arguments;
    char* panicString = NULL;

    va_start(arguments, format);
    vasprintf(&panicString, format, arguments);
    va_end(arguments);
    
    NSString* panicStringObj = [NSString stringWithUTF8String:panicString];
    free(panicString);

    _XCTFailureHandler(test, YES, "unknown file", 0, panicStringObj, @"");
}
