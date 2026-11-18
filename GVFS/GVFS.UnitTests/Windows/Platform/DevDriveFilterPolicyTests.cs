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
        private const string Attached = "\r\nFilters currently attached to this developer volume:\r\n    MsSecFlt, WdFilter";
        private const string NoAttached = "\r\nNo filters are currently attached to this developer volume.\r\n";

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
                Preamble + VolumeHeader + "    bindflt,\r\n    wcifs, DfmFlt\r\n" + Attached,
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
        [TestCase(Preamble)]
        [TestCase(Preamble + VolumeHeader + "    bindflt\r\n")]
        [TestCase(Preamble + VolumeHeader + "    bindflt,\r\n" + Attached)]
        [TestCase(Preamble + VolumeHeader + "    bindflt,\r\n\r\n    wcifs\r\n" + Attached)]
        [TestCase(Preamble + "\r\nFilters currently attached to this developer volume:\r\n")]
        [TestCase(Preamble + "\r\nFilters currently attached to this developer volume:\r\n    MsSecFlt,\r\n")]
        [TestCase(Preamble + Attached + "\r\n" + VolumeHeader + "    bindflt\r\n")]
        [TestCase(Preamble + Preamble + Attached)]
        [TestCase(Preamble + NoAttached + NoAttached)]
        [TestCase(Preamble + Attached + "\r\n" + NoAttached)]
        [TestCase(Preamble + NoAttached + VolumeHeader + "    bindflt\r\n")]
        [TestCase(Preamble + VolumeHeader + "    bindflt,\r\n" + NoAttached)]
        [TestCase("Developer volumes are enabled.\r\n")]
        [TestCase("This is a trusted developer volume.\r" + VolumeHeader + "    DfmFlt\r")]
        public void ParserRejectsAmbiguousOrUnrecognizedOutput(string output)
        {
            DevDriveFilterPolicy.TryParseAllowedFilters(output, out DevDriveFilterPolicy.Allowances parsed).ShouldBeFalse();
            parsed.ShouldBeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParserRejectsIncompleteAllowanceSectionsBeforeTheAttachedList(bool hasVolumeAllowance)
        {
            string volume = hasVolumeAllowance ? VolumeHeader + "    bindflt\r\n" : string.Empty;
            string complete = Preamble + Enterprise + Machine + volume + Attached;
            DevDriveFilterPolicy.TryParseAllowedFilters(complete, out DevDriveFilterPolicy.Allowances parsed).ShouldBeTrue();
            if (hasVolumeAllowance)
            {
                parsed.Volume.ShouldMatchInOrder("bindflt");
            }
            else
            {
                parsed.Volume.ShouldBeEmpty();
            }

            int attachedNames = complete.IndexOf("    MsSecFlt, WdFilter", System.StringComparison.Ordinal);
            for (int length = 0; length <= attachedNames; ++length)
            {
                bool accepted = DevDriveFilterPolicy.TryParseAllowedFilters(complete.Substring(0, length), out DevDriveFilterPolicy.Allowances _);
                Assert.That(accepted, Is.False, $"Accepted output truncated at character {length}.");
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ParserAcceptsRealAttachedOutputWithoutAFinalNewline(bool noAttachedFilters, bool disallowAntivirus)
        {
            string status = disallowAntivirus ? "Developer volumes may not be protected by antivirus filter.\r\n\r\n" : Enterprise;
            string output = Preamble + status + Machine + VolumeHeader + "    bindflt\r\n"
                + (noAttachedFilters ? NoAttached : Attached);
            DevDriveFilterPolicy.TryParseAllowedFilters(output, out DevDriveFilterPolicy.Allowances parsed).ShouldBeTrue();
            parsed.Volume.ShouldMatchInOrder("bindflt");
            parsed.Inherited.Contains("DfmFlt").ShouldBeTrue();
            parsed.AllowsProjFS.ShouldBeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AllowFilterReachesTheWriteWithRealTerminalOutput(bool noAttachedFilters)
        {
            string ending = noAttachedFilters ? NoAttached : Attached;
            Queue<ProcessResult> results = new Queue<ProcessResult>(new[]
            {
                Query(Preamble + Machine + VolumeHeader + "    bindflt\r\n" + ending),
                Query(Preamble + Machine + VolumeHeader + "    bindflt\r\n" + ending),
                Query(string.Empty),
                Query(Preamble + Machine + VolumeHeader + "    bindflt, PrjFlt\r\n" + ending),
            });
            bool wrote = false;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    if (results.Count == 2)
                    {
                        command.ShouldEqual("devdrv setfiltersallowed /volume mock:\\volume\\ \"bindflt,PrjFlt\"");
                        wrote = true;
                    }

                    return results.Dequeue();
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.Added);
            wrote.ShouldBeTrue();
            results.Count.ShouldEqual(0);
            error.ShouldBeNull();
        }

        [TestCase(1)]
        [TestCase(2)]
        public void AllowFilterNeverWritesAfterAnIncompleteQuery(int incompleteQuery)
        {
            int calls = 0;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(@"devdrv query mock:\volume\");
                    ++calls;
                    return Query(calls == incompleteQuery
                        ? Preamble + Enterprise + Machine
                        : Preamble + Enterprise + Machine + VolumeHeader + "    bindflt\r\n" + Attached);
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.PolicyUnreadable);
            calls.ShouldEqual(incompleteQuery);
            error.ShouldContain("not recognized");
        }

        [TestCase]
        public void AllowFilterDoesNotWriteWhenAQueryReportsAnErrorDespiteSuccessExit()
        {
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    command.ShouldEqual(@"devdrv query mock:\volume\");
                    return new ProcessResult(Preamble + Enterprise + Machine + Attached, "query warning", 0);
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.PolicyUnreadable);
            error.ShouldContain("query warning");
        }

        [TestCase]
        public void AllowFilterReportsAnUnverifiedWriteWhenTheReadbackReportsAnError()
        {
            int calls = 0;
            string output = Preamble + Enterprise + Machine + VolumeHeader + "    bindflt\r\n" + Attached;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command =>
                {
                    ++calls;
                    return calls == 4
                        ? new ProcessResult(output, "readback warning", 0)
                        : Query(output);
                },
                NullTracer.Instance,
                out string error).ShouldEqual(DevDriveFilterPolicy.AllowFilterResult.Failed);
            calls.ShouldEqual(4);
            error.ShouldContain("update was attempted", "verification failed", "readback warning");
        }

        [TestCase(0, DevDriveFilterPolicy.AllowFilterResult.Added)]
        [TestCase(5, DevDriveFilterPolicy.AllowFilterResult.RemountRequired)]
        [TestCase(-1, DevDriveFilterPolicy.AllowFilterResult.RemountRequired)]
        public void AllowFilterPreservesOnlyVolumeEntriesAndVerifiesBusyWrites(int writeExit, DevDriveFilterPolicy.AllowFilterResult expected)
        {
            string before = Preamble + Enterprise + Machine + VolumeHeader + "    bindflt\r\n" + Attached;
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
                Query(Preamble + Enterprise + Machine + VolumeHeader + "    bindflt,prjflt\r\n" + Attached),
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

        [TestCase(Preamble + Enterprise + "Filters allowed on any developer volume:\r\n    PrjFlt,DfmFlt\r\n" + Attached)]
        [TestCase(Preamble + "Filters allowed on any developer volume, by group policy:\r\n    PrjFlt\r\n" + Attached)]
        [TestCase(Preamble + VolumeHeader + "    prjflt\r\n" + Attached)]
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
                Query(Preamble + Enterprise + Machine + Attached),
                Query(Preamble + Enterprise + Machine + Attached),
                Query(string.Empty),
                Query(Preamble + Enterprise + Machine + VolumeHeader + "    PrjFlt\r\n" + Attached),
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
                    return Query(++calls == 1 ? Preamble + Attached : Preamble + VolumeHeader + "    wcifs\r\n" + Attached);
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
        [TestCase(0, Preamble + Enterprise + Machine, "verification failed")]
        [TestCase(0, Preamble + Attached, "does not contain")]
        public void AllowFilterDoesNotReportAnUnverifiedWriteAsSuccess(int verifyExit, string output, string message)
        {
            int calls = 0;
            DevDriveFilterPolicy.TryAllowFilter(
                Root,
                command => ++calls < 4 ? Query(Preamble + Attached) : new ProcessResult(output, string.Empty, verifyExit),
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
