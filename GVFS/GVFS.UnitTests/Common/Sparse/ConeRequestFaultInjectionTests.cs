using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConeRequestFaultInjectionTests
    {
        private const string FaultFile = @"C:\faults.txt";

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void IsOffWithoutTheEnvironmentVariable(string value)
        {
            ConeRequestFaultInjection.FromEnvironment(name => value, path => throw new AssertionException("must not read a file")).ShouldBeNull();
        }

        [TestCase]
        public void ReadsTheFaultFileAtEachRequest()
        {
            string content = null;
            int reads = 0;
            ConeRequestFaultInjection faults = ConeRequestFaultInjection.FromEnvironment(
                name => name == ConeRequestFaultInjection.FaultFileEnvironmentVariable ? FaultFile : null,
                path =>
                {
                    path.ShouldEqual(FaultFile);
                    reads++;
                    return content;
                });

            faults.GetForcedReply(NamedPipeMessages.ConeManagement.WidenRequest).ShouldBeNull();

            content = "ConeWiden F\r\nConeApply NL\n";
            faults.GetForcedReply(NamedPipeMessages.ConeManagement.WidenRequest).ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);
            faults.GetForcedReply(NamedPipeMessages.ConeManagement.ApplyRequest).ShouldEqual(NamedPipeMessages.ConeManagement.NotLockHolderResult);

            content = string.Empty;
            faults.GetForcedReply(NamedPipeMessages.ConeManagement.WidenRequest).ShouldBeNull();
            reads.ShouldEqual(4);
        }

        [TestCase]
        public void NeverForcesANarrow()
        {
            ConeRequestFaultInjection faults = ConeRequestFaultInjection.FromEnvironment(name => FaultFile, path => "ConeNarrow F\n");

            faults.GetForcedReply(NamedPipeMessages.ConeManagement.NarrowRequest).ShouldBeNull();
        }

        [TestCase]
        public void AFileThatCannotBeReadIsNoFault()
        {
            ConeRequestFaultInjection faults = ConeRequestFaultInjection.FromEnvironment(name => FaultFile, path => throw new IOException("in use"));

            faults.GetForcedReply(NamedPipeMessages.ConeManagement.WidenRequest).ShouldBeNull();
        }

        [TestCase("ConeWiden S")]
        [TestCase("ConeWiden")]
        [TestCase("ConeWiden F extra")]
        [TestCase("ConeNarrow F")]
        [TestCase("conewiden F")]
        public void IgnoresLinesThatAreNotValid(string line)
        {
            ConeRequestFaultInjection.ParseFaults(line).ShouldBeEmpty();
        }

        [TestCase]
        public void ParsesValidLines()
        {
            Dictionary<string, string> faults = ConeRequestFaultInjection.ParseFaults("  ConeApply   F  \n\nConeWiden NL");

            faults[NamedPipeMessages.ConeManagement.ApplyRequest].ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);
            faults[NamedPipeMessages.ConeManagement.WidenRequest].ShouldEqual(NamedPipeMessages.ConeManagement.NotLockHolderResult);
        }
    }
}
