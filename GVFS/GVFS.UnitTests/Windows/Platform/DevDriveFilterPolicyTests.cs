using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.UnitTests.Windows.Platform
{
    [TestFixture]
    public class DevDriveFilterPolicyTests
    {
        private const string Root = @"mock:\volume\";
        private const string Preamble = "This is a trusted developer volume.\r\n\r\n";
        private const string Enterprise =
            "Developer volumes are protected by antivirus filter, by group policy.\r\n\r\n" +
            "Filters allowed on any developer volume, by group policy:\r\n    MsSecFlt\r\n\r\n";
        private const string Machine = "Filters allowed on any developer volume:\r\n    DfmFlt\r\n\r\n";
        private const string VolumeHeader = "Filters allowed on this developer volume:\r\n";

        [TestCase]
        public void ParserSeparatesVolumeAllowancesFromInheritedAndAttachedFilters()
        {
            string output = Preamble + Enterprise + Machine + VolumeHeader + "    bindflt\r\n\r\n" +
                "Filters currently attached to this developer volume:\r\n    WdFilter\r\n";
            DevDriveFilterPolicy.TryParseAllowedFilters(output, out DevDriveFilterPolicy.Allowances parsed).ShouldBeTrue();
            parsed.Volume.ShouldMatchInOrder("bindflt");
            parsed.Inherited.ShouldMatchInOrder("MsSecFlt", "DfmFlt");
            parsed.AllowsProjFS.ShouldBeFalse();
        }

        [TestCase]
        public void ParserPreservesEveryLineOfVolumeAllowances()
        {
            DevDriveFilterPolicy.TryParseAllowedFilters(
                Preamble + VolumeHeader + "    bindflt,\r\n    wcifs, DfmFlt\r\n",
                out DevDriveFilterPolicy.Allowances parsed).ShouldBeTrue();
            parsed.Volume.ShouldMatchInOrder("bindflt", "wcifs", "DfmFlt");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(Preamble + VolumeHeader)]
        [TestCase(Preamble + VolumeHeader + "    DfmFlt,,wcifs\r\n")]
        [TestCase(Preamble + VolumeHeader + "    DfmFlt,,\r\n")]
        [TestCase(Preamble + VolumeHeader + "    PrjFlt DfmFlt\r\n")]
        [TestCase(Preamble + VolumeHeader + "    DfmFlt,\" /f\r\n")]
        [TestCase(Preamble + VolumeHeader + "    DfmFlt\r\n    unrecognized continuation text\r\n")]
        [TestCase(Preamble + VolumeHeader + "    DfmFlt\r\n\r\n" + VolumeHeader + "    wcifs\r\n")]
        [TestCase(Preamble + VolumeHeader + "    PrjFl\u0442\r\n")]
        [TestCase("Developer volumes are enabled.\r\n")]
        [TestCase("This is a trusted developer volume.\r" + VolumeHeader + "    DfmFlt\r")]
        public void ParserRejectsAmbiguousOrUnrecognizedOutput(string output)
        {
            DevDriveFilterPolicy.TryParseAllowedFilters(output, out DevDriveFilterPolicy.Allowances parsed).ShouldBeFalse();
            parsed.ShouldBeNull();
        }

        [TestCase(0, DevDriveFilterPolicy.AllowFilterResult.Added)]
        [TestCase(5, DevDriveFilterPolicy.AllowFilterResult.RemountRequired)]
        [TestCase(-1, DevDriveFilterPolicy.AllowFilterResult.RemountRequired)]
        public void AllowFilterPreservesOnlyVolumeEntriesAndVerifiesBusyWrites(int writeExit, DevDriveFilterPolicy.AllowFilterResult expected)
        {
            string before = Preamble + Enterprise + Machine + VolumeHeader + "    bindflt\r\n";
            Queue<string> commands = new Queue<string>(new[]
            {
                @"devdrv query mock:\volume\",
                @"devdrv query mock:\volume\",
                "devdrv setfiltersallowed /volume mock:\\volume\\ \"bindflt,PrjFlt\"",
                @"devdrv query mock:\volume\",
            });
            Queue<ProcessResult> results = new Queue<ProcessResult>(new[]
            {
                Query(before),
                Query(before),
                new ProcessResult("Volume is in use.", string.Empty, writeExit),
                Query(Preamble + Enterprise + Machine + VolumeHeader + "    bindflt,prjflt\r\n"),
            });
            MockTracer tracer = new MockTracer();

            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(commands.Dequeue());
                    return results.Dequeue();
                },
                tracer,
                out string error).ShouldEqual(expected);
            error.ShouldBeNull();
            commands.Count.ShouldEqual(0);
            tracer.RelatedEventNames.ShouldMatchInOrder(
                "DevDriveFilterPolicy_UpdateAttempt", "DevDriveFilterPolicy_UpdateResult", "DevDriveFilterPolicy_UpdateVerified");
            tracer.RelatedEventMetadata.Last()["PreviousVolumeFilters"].ShouldEqual("bindflt");
            tracer.RelatedEventMetadata.Last()["SavedVolumeFilters"].ShouldEqual("bindflt,prjflt");
            tracer.RelatedWarningEvents.Count.ShouldEqual(writeExit == 0 ? 0 : 1);
        }

        [TestCase(Preamble + Enterprise + "Filters allowed on any developer volume:\r\n    PrjFlt,DfmFlt\r\n")]
        [TestCase(Preamble + "Filters allowed on any developer volume, by group policy:\r\n    PrjFlt\r\n")]
        [TestCase(Preamble + VolumeHeader + "    prjflt\r\n")]
        public void AllowFilterDoesNotWriteWhenAnyApplicableListAlreadyAllowsFilter(string output)
        {
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(@"devdrv query mock:\volume\");
                    return Query(output);
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.AlreadyAllowed);
            error.ShouldBeNull();
        }

        [TestCase]
        public void AllowFilterAddsOnlyPrjFltWhenVolumeListIsAbsent()
        {
            Queue<ProcessResult> results = new Queue<ProcessResult>(new[]
            {
                Query(Preamble + Enterprise + Machine),
                Query(Preamble + Enterprise + Machine),
                Query(string.Empty),
                Query(Preamble + Enterprise + Machine + VolumeHeader + "    PrjFlt\r\n"),
            });
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    if (results.Count == 2)
                    {
                        command.ShouldEqual("devdrv setfiltersallowed /volume mock:\\volume\\ \"PrjFlt\"");
                    }

                    return results.Dequeue();
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.Added);
            results.Count.ShouldEqual(0);
            error.ShouldBeNull();
        }

        [TestCase]
        public void AllowFilterAbortsWhenThePreWriteRecheckObservesAChange()
        {
            int calls = 0;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(@"devdrv query mock:\volume\");
                    return Query(++calls == 1 ? Preamble : Preamble + VolumeHeader + "    wcifs\r\n");
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.Failed);
            calls.ShouldEqual(2);
            error.ShouldContain("changed during the request");
        }

        [TestCase(5, "Access denied.")]
        [TestCase(-1, "Timed out.")]
        [TestCase(0, "Unrecognized output.")]
        public void AllowFilterDoesNotWriteWhenTheInitialQueryFails(int exitCode, string output)
        {
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(@"devdrv query mock:\volume\");
                    return new ProcessResult(output, string.Empty, exitCode);
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.PolicyUnreadable);
            error.ShouldNotBeNull();
        }

        [TestCase(5, "Access denied.", "verification failed")]
        [TestCase(0, "Unrecognized output.", "verification failed")]
        [TestCase(0, Preamble, "does not contain")]
        public void AllowFilterDoesNotReportAnUnverifiedWriteAsSuccess(int verifyExit, string output, string message)
        {
            int calls = 0;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command => ++calls < 4 ? Query(Preamble) : new ProcessResult(output, string.Empty, verifyExit),
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.Failed);
            calls.ShouldEqual(4);
            error.ShouldContain(message);
        }

        private static ProcessResult Query(string output)
        {
            return new ProcessResult(output, string.Empty, 0);
        }
    }
}
