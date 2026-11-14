using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class ConeManagementMessagesTests
    {
        [TestCase]
        public void WidenRequestRoundTripsThroughMessageFrame()
        {
            NamedPipeMessages.ConeManagement.WidenParameters original =
                new NamedPipeMessages.ConeManagement.WidenParameters(
                    "4242",
                    new List<string> { "A/b.txt", "my dir/a|b.txt", "C/D/" });

            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(
                new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.WidenRequest, original.ToBody()).ToString());

            message.Header.ShouldEqual(NamedPipeMessages.ConeManagement.WidenRequest);
            NamedPipeMessages.ConeManagement.WidenParameters.TryParse(
                message.Body, out NamedPipeMessages.ConeManagement.WidenParameters parsed)
                .ShouldBeTrue();
            parsed.CommandId.ShouldEqual("4242");
            parsed.Paths.ShouldMatchInOrder("A/b.txt", "my dir/a|b.txt", "C/D/");
        }

        [TestCase]
        public void WidenAllowsAnEmptyCommandId()
        {
            string body = new NamedPipeMessages.ConeManagement.WidenParameters(null, new List<string> { "A/b.txt" }).ToBody();

            NamedPipeMessages.ConeManagement.WidenParameters.TryParse(body, out NamedPipeMessages.ConeManagement.WidenParameters parsed).ShouldBeTrue();
            parsed.CommandId.ShouldEqual(string.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("4242")]
        [TestCase("12a\0A/b.txt")]
        [TestCase("4242\0/abs/path.txt")]
        [TestCase("4242\0C:/Windows/x.txt")]
        [TestCase("4242\0D:relative.txt")]
        [TestCase("4242\0//server/share/x.txt")]
        [TestCase("4242\0A\\b.txt")]
        [TestCase("4242\0../outside.txt")]
        [TestCase("4242\0A/../../outside.txt")]
        [TestCase("4242\0A//b.txt")]
        [TestCase("4242\0./A/b.txt")]
        [TestCase("4242\0A/b.txt\0")]
        [TestCase("4242\0A/line\nbreak.txt")]
        [TestCase("4242\0A/frame\u0003end.txt")]
        [TestCase("4242\0A/control\u0001.txt")]
        [TestCase("4242\0A/*/x.txt")]
        [TestCase("4242\0A/b?/")]
        [TestCase("4242\0A/trailing./x.txt")]
        [TestCase("4242\0A/trailing /x.txt")]
        [TestCase("4242\0CON")]
        [TestCase("4242\0A/nul.txt")]
        [TestCase("4242\0A/COM1/")]
        [TestCase("4242\0.git/config")]
        [TestCase("12345678901\0A/b.txt")]
        public void WidenParseRejectsBodiesThatAreNotRepoRelativePaths(string body)
        {
            NamedPipeMessages.ConeManagement.WidenParameters.TryParse(body, out NamedPipeMessages.ConeManagement.WidenParameters parsed)
                .ShouldBeFalse();
            parsed.ShouldBeNull();
        }

        [TestCase]
        public void WidenParseRejectsAnUnpairedSurrogate()
        {
            NamedPipeMessages.ConeManagement.WidenParameters.TryParse("4242\0A/" + '\uD800' + ".txt", out NamedPipeMessages.ConeManagement.WidenParameters parsed)
                .ShouldBeFalse();
            parsed.ShouldBeNull();
        }

        [TestCase("A/*.cs")]
        [TestCase("A/COM10.txt")]
        [TestCase("A/console/")]
        [TestCase("A/.gitignore")]
        [TestCase("A/\uD83D\uDE00/b.txt")]
        public void WidenParseAcceptsUnusualButValidPaths(string path)
        {
            NamedPipeMessages.ConeManagement.WidenParameters.TryParse("4242\0" + path, out NamedPipeMessages.ConeManagement.WidenParameters parsed)
                .ShouldBeTrue();
            parsed.Paths.ShouldMatchInOrder(path);
        }

        [TestCase("4242", true)]
        [TestCase("", true)]
        [TestCase(null, false)]
        [TestCase("12a", false)]
        [TestCase("-1", false)]
        public void NarrowParseAcceptsOnlyDigits(string body, bool expected)
        {
            NamedPipeMessages.ConeManagement.NarrowParameters.TryParse(body, out NamedPipeMessages.ConeManagement.NarrowParameters parsed)
                .ShouldEqual(expected);

            if (expected)
            {
                parsed.CommandId.ShouldEqual(body);
                parsed.ToBody().ShouldEqual(body);
            }
        }
    }
}
