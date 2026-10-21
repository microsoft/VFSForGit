using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class HydrationStatusDiagnosticsTests
    {
        [TestCase]
        public void CacheExtensionPreservesLegacyCountPrefix()
        {
            Guid calculationId = Guid.NewGuid();
            NamedPipeMessages.HydrationStatus.Response response = new NamedPipeMessages.HydrationStatus.Response
            {
                PlaceholderFileCount = 1,
                PlaceholderFolderCount = 2,
                ModifiedFileCount = 3,
                ModifiedFolderCount = 4,
                TotalFileCount = 100,
                TotalFolderCount = 50,
            };
            NamedPipeMessages.HydrationStatus.CacheInfo info = new NamedPipeMessages.HydrationStatus.CacheInfo
            {
                State = NamedPipeMessages.HydrationStatus.CacheState.Available,
                CalculationId = calculationId,
                AgeMs = 123,
                Invalidated = true,
                CountsComplete = true,
            };
            string body = response.ToBody(info);
            Assert.That(body, Does.StartWith("1,2,3,4,100,50,D1,"));
            Assert.IsTrue(NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response parsed));
            Assert.AreEqual(100, parsed.TotalFileCount);
            Assert.IsTrue(NamedPipeMessages.HydrationStatus.CacheInfo.TryParse(body, out NamedPipeMessages.HydrationStatus.CacheInfo parsedInfo, hasCounts: true));
            Assert.AreEqual(calculationId, parsedInfo.CalculationId);
            Assert.AreEqual(123, parsedInfo.AgeMs);
            Assert.IsTrue(parsedInfo.Invalidated);
            Assert.IsTrue(parsedInfo.CountsComplete);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("D2,1,-,0,0,1")]
        [TestCase("D1,999,-,0,0,1")]
        [TestCase("D1,1,private-path,0,0,1")]
        [TestCase("D1,1,-,-2,0,1")]
        [TestCase("D1,1,-,0,yes,1")]
        [TestCase("D1,1,-,0,0,yes")]
        public void CacheExtensionRejectsUnknownOrMalformedFields(string body)
        {
            Assert.IsFalse(NamedPipeMessages.HydrationStatus.CacheInfo.TryParse(body, out _));
        }

        [TestCase(null, "MalformedResponse")]
        [TestCase("1,2", "MalformedResponse")]
        [TestCase("1,2,secret,4,100,50", "MalformedResponse")]
        [TestCase("1,2,3,4,100,1", "InvalidCounts")]
        [TestCase("1,2,3,4,100,50", "Success")]
        public void CacheResultDistinguishesMalformedAndInvalidCounts(string body, string outcome)
        {
            HealthVerb.CacheLookupResult result = HealthVerb.ParseCachedResponse(
                new NamedPipeMessages.Message("S", body));
            Assert.AreEqual(outcome, result.Outcome.ToString());
            Assert.IsNull(result.Info);
            Assert.IsFalse((bool)result.GetMetadata()["DiagnosticsAvailable"]);
            Assert.That(EventMetadataConverter.SerializeToString(result.GetMetadata()), Does.Not.Contain("secret"));
            if (body != null)
            {
                Assert.AreEqual(1, result.Counts["PlaceholderFileCount"]);
            }
        }

        [TestCase]
        public void CacheMissRetainsTheFailedCalculationIdentity()
        {
            NamedPipeMessages.HydrationStatus.CacheInfo info = new NamedPipeMessages.HydrationStatus.CacheInfo
            {
                State = NamedPipeMessages.HydrationStatus.CacheState.InvalidCounts,
                CalculationId = Guid.NewGuid(),
                AgeMs = 1234,
            };
            HealthVerb.CacheLookupResult result = HealthVerb.ParseCachedResponse(new NamedPipeMessages.Message("NA", info.ToBody()));
            Assert.AreEqual(HealthVerb.CacheLookupOutcome.NotAvailable, result.Outcome);
            Assert.AreEqual(info.CalculationId, result.Info.CalculationId);
            Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.InvalidCounts, result.Info.State);
            Assert.IsNull(result.Counts);
        }

        [TestCase]
        public void CachedDisplayRecordsCountsWithoutFallback()
        {
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                CachedHealthVerb verb = new CachedHealthVerb { Output = output };
                EventMetadata metadata = new EventMetadata();
                verb.OutputHydrationPercent(null, tracer, metadata, Guid.NewGuid());
                Assert.AreEqual("Cache", metadata["ResultSource"]);
                Assert.AreEqual("Percentage", metadata["DisplayOutcome"]);
                Assert.AreEqual("LegacyCache", metadata["Outcome"]);
                EventMetadata cache = (EventMetadata)metadata["Cache"];
                Assert.AreEqual(HealthVerb.CacheLookupOutcome.Success, cache["Outcome"]);
                Assert.AreEqual(100, ((EventMetadata)cache["Counts"])["TotalFileCount"]);
                Assert.That(output.ToString(), Does.Contain("4% of files and 12% of folders hydrated."));
            }
        }

        [TestCase]
        public void DiagnosticArithmeticDoesNotOverflowOrHideMultipleFailures()
        {
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics
            {
                PlaceholderFileCount = int.MaxValue,
                ModifiedFileCount = int.MaxValue,
                TotalFileCount = 1,
                PlaceholderFolderCount = 2,
                ModifiedFolderCount = 3,
                TotalFolderCount = 1,
            };
            HydrationValidationFailure failures = diagnostics.GetValidationFailures();
            Assert.IsTrue(failures.HasFlag(HydrationValidationFailure.FileSumOverflow));
            Assert.IsTrue(failures.HasFlag(HydrationValidationFailure.HydratedFilesExceedTotal));
            Assert.IsTrue(failures.HasFlag(HydrationValidationFailure.HydratedFoldersExceedTotal));
            Assert.AreEqual(4294967294L, diagnostics.GetMetadata()["HydratedFileCount"]);
        }

        [TestCase]
        public void DiskDiagnosticsObserveOperationsWithoutChangingReplaySemantics()
        {
            using (Stream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("A a\nD a\nA b/\ninvalid\n")))
            using (StreamReader streamReader = new StreamReader(stream))
            {
                HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
                string[] lines = new EnlistmentPathData().ReadModifiedPathDatabaseLines(streamReader, diagnostics).ToArray();
                Assert.AreEqual(new[] { "a", "D a", "b/", "invalid" }, lines);
                Assert.AreEqual(2, diagnostics.ModifiedPathsAddRecords);
                Assert.AreEqual(1, diagnostics.ModifiedPathsDeleteRecords);
                Assert.AreEqual(1, diagnostics.ModifiedPathsOtherRecords);
            }
        }

        private sealed class CachedHealthVerb : HealthVerb
        {
            internal override CacheLookupResult TryGetCachedHydrationStatus(GVFSEnlistment enlistment)
            {
                return ParseCachedResponse(new NamedPipeMessages.Message("S", "1,2,3,4,100,50"));
            }

            internal override EnlistmentHydrationSummary CreateHydrationSummary(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics)
            {
                Assert.Fail("A cached result must not calculate a fallback.");
                return null;
            }
        }
    }
}
