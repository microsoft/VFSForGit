#include "../PrjFSLib/Json/JsonWriter.hpp"
#include <utility>
#include <vector>
#import <XCTest/XCTest.h>

using std::make_pair;
using std::pair;
using std::string;
using std::vector;

@interface JsonWriterTests : XCTestCase
@end

@implementation JsonWriterTests
{
}

- (void) setUp
{
}

- (void) tearDown
{
}

- (void) testAddNothing {
    string expectedResult = "{}";
    JsonWriter writer;
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testAddString {
    string expectedResult = "{\"testKey\":\"testValue\"}";
    JsonWriter writer;
    writer.Add("testKey", "testValue");
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testAddInt32 {
    string expectedResult = "{\"testKey\":32}";
    JsonWriter writer;
    writer.Add("testKey", 32);
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testAddUInt32 {
    string expectedResult = "{\"testKey\":32}";
    JsonWriter writer;
    writer.Add("testKey", static_cast<uint32_t>(32));
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testAddUInt64 {
    string expectedResult = "{\"testKey\":32}";
    JsonWriter writer;
    writer.Add("testKey", static_cast<uint64_t>(32));
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testAddMultiplePairs {
    string expectedResult =
    "{"
        "\"testKey\":\"testValue\","
        "\"testInt\":32,"
        "\"testUInt64\":9223372036854775807,"
        "\"test2ndString\":\"testValue2\""
    "}";
    JsonWriter writer;
    writer.Add("testKey", "testValue");
    writer.Add("testInt", 32);
    writer.Add("testUInt64", 9223372036854775807ULL);
    writer.Add("test2ndString", "testValue2");
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testEscapingCharacters {
    vector<pair<char, string>> escapedCharacters =
    {
        make_pair('\"', "\\\""),
        make_pair('\\', "\\\\"),
        make_pair('\n', "\\n"),
        make_pair('\r', "\\r"),
        make_pair('\t', "\\t"),
        make_pair('\f', "\\f"),
        make_pair('\b', "\\b"),
        make_pair(static_cast<char>(2), "\\u0002"),
        make_pair(static_cast<char>(0x13), "\\u0013"),
        make_pair(static_cast<char>(0x20), " "),
    };

    for (const pair<char, string>& charPair : escapedCharacters)
    {
        string expectedResult =
        "{"
            "\"testKey\":\"" + charPair.second + "\""
        "}";
    
        JsonWriter writer;
        writer.Add("testKey", string(1, charPair.first));
        string jsonResult = writer.ToString();
        
        XCTAssertTrue(
            jsonResult == expectedResult,
            "%s",
            ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
    }
}

- (void) testMultipleEscapedCharacters {
    string expectedResult =
    "{"
        "\"testKey1\":\"testLine1\\r\\nTestList2\","
        "\"testKey2\":\"\\f\\t content\""
    "}";
    JsonWriter writer;
    writer.Add("testKey1", "testLine1\r\nTestList2");
    writer.Add("testKey2", "\f\t content");
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testEscapedCharactersInKeys {
    string expectedResult =
    "{"
        "\"testKeyLine1\\r\\nTestKeyList2\":\"testValue\","
        "\"\\f\\t key\":32"
    "}";
    JsonWriter writer;
    writer.Add("testKeyLine1\r\nTestKeyList2", "testValue");
    writer.Add("\f\t key", 32);
    string jsonResult = writer.ToString();
    XCTAssertTrue(
        jsonResult == expectedResult,
        "%s",
        ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
}

- (void) testNestedJson {
    {
        string expectedResult =
        "{"
            "\"testKey\":\"testdata\","
            "\"jsonPayload\":{\"payloadKey\":32}"
        "}";
        JsonWriter writer;
        writer.Add("testKey", "testdata");
        JsonWriter payloadWriter;
        payloadWriter.Add("payloadKey", 32);
        writer.Add("jsonPayload", payloadWriter);
        string jsonResult = writer.ToString();
        XCTAssertTrue(
            jsonResult == expectedResult,
            "%s",
            ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
    }
    
    {
        string expectedResult =
        "{"
            "\"jsonPayload\":{\"payloadKey\":32},"
            "\"testKey\":\"testdata\""
        "}";
        JsonWriter writer;
        JsonWriter payloadWriter;
        payloadWriter.Add("payloadKey", 32);
        writer.Add("jsonPayload", payloadWriter);
        writer.Add("testKey", "testdata");
        string jsonResult = writer.ToString();
        XCTAssertTrue(
            jsonResult == expectedResult,
            "%s",
            ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
    }
    
    {
        string expectedResult =
        "{"
            "\"testKey\":\"testdata\","
            "\"jsonPayload\":{\"nestedPayload\":{\"nestedKey\":\"nestedString\"}},"
            "\"lastKey\":64"
        "}";
        JsonWriter writer;
        writer.Add("testKey", "testdata");
        JsonWriter nestedPayloadWriter;
        nestedPayloadWriter.Add("nestedKey", "nestedString");
        JsonWriter payloadWriter;
        payloadWriter.Add("nestedPayload", nestedPayloadWriter);
        writer.Add("jsonPayload", payloadWriter);
        writer.Add("lastKey", 64);
        string jsonResult = writer.ToString();
        XCTAssertTrue(
            jsonResult == expectedResult,
            "%s",
            ("Expected result: " + expectedResult + " Result: " + jsonResult).c_str());
    }
}

@end
