#import <XCTest/XCTest.h>
#import "VFSMessageParser.h"

@interface MessageParserTests : XCTestCase
@end

@implementation MessageParserTests

- (void)testParsingValidMessageSucceeds
{
    NSDictionary *expectedDict = [self validMessage];
    NSData *messageData =
    [NSJSONSerialization dataWithJSONObject:expectedDict
                                    options:NSJSONWritingPrettyPrinted
                                      error:nil];
    
    NSError *error;
    NSDictionary *parsedMessage;
    XCTAssertTrue([VFSMessageParser tryParseData:messageData
                                         message:&parsedMessage
                                           error:&error]);
    XCTAssertNil(error);
    
    [self validateParsedMessage:parsedMessage expectedMessage:expectedDict];
}

- (void)testParsingMessageWithTrailingCtrlCharsSucceed
{
    NSDictionary *expectedDict = [self validMessage];
    NSData *messageData =
    [NSJSONSerialization dataWithJSONObject:expectedDict
                                    options:NSJSONWritingPrettyPrinted
                                      error:nil];
    NSMutableData *dataWithCtrlChars = [NSMutableData dataWithData:messageData];
    NSString *stringWithCtrlChars = [NSString stringWithFormat:@"%c%c%c%c%c%c%c",
                                     0x07,
                                     0x08,
                                     0x1B,
                                     0x0C,
                                     0x0A,
                                     0x0D,
                                     0x09];
    [dataWithCtrlChars appendData:[stringWithCtrlChars
                                   dataUsingEncoding:NSUTF8StringEncoding]];
    
    NSError *error;
    NSDictionary *parsedMessage;
    XCTAssertTrue([VFSMessageParser tryParseData:dataWithCtrlChars
                                         message:&parsedMessage
                                           error:&error]);
    XCTAssertNil(error);
    
    [self validateParsedMessage:parsedMessage expectedMessage:expectedDict];
}

- (void)testParsingMalformedMessageFails
{
    NSString *message = @"{ \"Id\", \"Message\", \"Foobar\"}";
    NSError *error;
    NSDictionary *parsedMessage;
    XCTAssertFalse([VFSMessageParser tryParseData:[message dataUsingEncoding:NSUTF8StringEncoding]
                                          message:&parsedMessage
                                            error:&error]);
    XCTAssertNil(parsedMessage);
    XCTAssertNotNil(error);
}

- (void)testParsingEmptyMessageFails
{
    NSString *message = @"";
    NSError *error;
    NSDictionary *parsedMessage;
    
    XCTAssertFalse([VFSMessageParser tryParseData:[message dataUsingEncoding:NSUTF8StringEncoding]
                                          message:&parsedMessage
                                            error:&error]);
    XCTAssertNil(parsedMessage);
    XCTAssertNotNil(error);
}

#pragma mark Utility Methods

- (NSDictionary *)validMessage
{
    NSInteger messageId = 1;
    NSString *title = @"GVFS Mount";
    NSString *message = @"Successfully mount repo";
    NSString *enlistment = @"/Users/foo/bar";
    NSInteger enlistmentCount = 0;
    NSDictionary *validDict = @{
                                @"Id" : [NSNumber numberWithLong:messageId],
                                @"Title" : title,
                                @"Message" : message,
                                @"Enlistment" : enlistment,
                                @"EnlistmentCount" : [NSNumber numberWithLong:enlistmentCount]
                                };
    return validDict;
}

- (BOOL)validateParsedMessage:(NSDictionary *)messageDict
              expectedMessage:(NSDictionary *)expectedDict
{
    XCTAssertNotNil(messageDict, @"Parse error: failure parsing message");
    
    [messageDict enumerateKeysAndObjectsUsingBlock:^(id  _Nonnull key,
                                                     id  _Nonnull obj,
                                                     BOOL * _Nonnull stop)
     {
         XCTAssertEqualObjects(obj,
                               expectedDict[key],
                               @"Parse error: mismatch in values of %@",
                               key);
     }];
}
@end
