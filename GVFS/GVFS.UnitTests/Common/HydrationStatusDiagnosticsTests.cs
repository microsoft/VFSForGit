using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using GVFS.UnitTests.Category;

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
            Assert.AreEqual("1,2,3,4,100,50,D1,1," + calculationId.ToString("N") + ",123,1,1", body);
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
                Assert.AreEqual(HealthVerb.HealthCommandOutcome.Success, metadata["Outcome"]);
                Assert.IsNull(metadata["CalculationOutcome"]);
                EventMetadata cache = (EventMetadata)metadata["Cache"];
                Assert.AreEqual(HealthVerb.CacheLookupOutcome.Success, cache["Outcome"]);
                Assert.AreEqual(100, ((EventMetadata)cache["Counts"])["TotalFileCount"]);
                Assert.That(output.ToString(), Does.Contain("4% of files and 12% of folders hydrated."));
            }
        }

        [TestCase("NotComputed", 0)]
        [TestCase("Available", 1)]
        [TestCase("ProviderUnavailable", 2)]
        [TestCase("FeatureDisabled", 3)]
        [TestCase("CircuitBreakerDisabled", 4)]
        [TestCase("InvalidCounts", 5)]
        [TestCase("IncompleteData", 6)]
        [TestCase("Cancelled", 7)]
        [TestCase("Failed", 8)]
        public void CacheStateIntegersAreWireContracts(string state, int value)
        {
            Assert.AreEqual(value, (int)Enum.Parse<NamedPipeMessages.HydrationStatus.CacheState>(state));
            Assert.AreEqual("D1," + value + ",-,-1,0,0",
                new NamedPipeMessages.HydrationStatus.CacheInfo { State = (NamedPipeMessages.HydrationStatus.CacheState)value }.ToBody());
            Assert.IsTrue(NamedPipeMessages.HydrationStatus.CacheInfo.TryParse("D1," + value + ",-,-1,0,0", out NamedPipeMessages.HydrationStatus.CacheInfo parsed));
            Assert.AreEqual(-1, parsed.AgeMs);
        }

        [TestCase("NA", null, "NotAvailable", "Absent")]
        [TestCase("NA", "private-path", "NotAvailable", "Unrecognized")]
        [TestCase("S", "1,2,3,4,100,50,D2,private", "Success", "Unrecognized")]
        [TestCase("unknown", "private", "UnexpectedResponse", "Absent")]
        [TestCase("S", "1,2,3,4,100,50,D1,999,-,0,0,0", "Success", "Unrecognized")]
        [TestCase("S", "1,2,3,4,100,50,D1,1,private,-2,0,0", "Success", "Unrecognized")]
        public void UnknownResponsesPreserveSafeProvenance(string header, string body, string outcome, string diagnostics)
        {
            HealthVerb.CacheLookupResult result = HealthVerb.ParseCachedResponse(new NamedPipeMessages.Message(header, body));
            Assert.AreEqual(outcome, result.Outcome.ToString());
            Assert.AreEqual(diagnostics, result.DiagnosticsStatus.ToString());
            Assert.That(EventMetadataConverter.SerializeToString(result.GetMetadata()), Does.Not.Contain("private"));
        }

        [TestCase]
        public void AdversarialResponsesCannotCopyRawTextIntoTelemetry()
        {
            string[] bodies =
            {
                null,
                string.Empty,
                ",,,,,",
                "1,2,3,4,100,50,D1,99,-,0,0,0",
                "1,2,3,4,100,50,D1,1,private,0,0,0",
                "1,2,3,4,100,50,D1,1,{01234567-89ab-cdef-0123-456789abcdef},0,0,0",
                "1,2,3,4,100,50,D1,1,-,9223372036854775808,0,0",
                "2147483648,0,0,0,0,0",
                "2147483647,0,2147483647,0,1,1",
                new string(',', 1024 * 1024),
            };
            foreach (string body in bodies)
            {
                HealthVerb.CacheLookupResult result = HealthVerb.ParseCachedResponse(new NamedPipeMessages.Message("S", body));
                string payload = EventMetadataConverter.SerializeToString(result.GetMetadata());
                Assert.That(payload, Does.Not.Contain("private"));
                Assert.That(payload.Length, Is.LessThan(2048));
                Assert.IsNull(result.Info);
                Assert.IsTrue(result.Counts.Values.All(value => value == null || value is int));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CacheHitKeepsCommandOutcomeSeparateFromCompleteness(bool complete)
        {
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                NamedPipeMessages.HydrationStatus.CacheInfo info = new NamedPipeMessages.HydrationStatus.CacheInfo
                {
                    CountsComplete = complete,
                    State = complete ? NamedPipeMessages.HydrationStatus.CacheState.Available : NamedPipeMessages.HydrationStatus.CacheState.IncompleteData,
                    CalculationId = Guid.NewGuid(),
                };
                WorkerHealthVerb verb = new WorkerHealthVerb { Output = output };
                verb.Pipe.Response = new NamedPipeMessages.Message("S", "1,2,3,4,100,50," + info.ToBody());
                EventMetadata metadata = new EventMetadata();
                verb.OutputHydrationPercent(null, tracer, metadata, Guid.NewGuid());
                Assert.AreEqual(HealthVerb.HealthCommandOutcome.Success, metadata["Outcome"]);
                Assert.AreEqual(complete ? HydrationSummaryOutcome.Success : HydrationSummaryOutcome.IncompleteData, metadata["CalculationOutcome"]);
                Assert.AreEqual(info.CalculationId.Value.ToString("N"), metadata["CalculationId"]);
                Assert.AreEqual("Cache", metadata["ResultSource"]);
                Assert.IsTrue(verb.Pipe.Disposed);
            }
        }

        [TestCase("ConnectTimeout", "Connect")]
        [TestCase("ConnectFailed", "Connect")]
        [TestCase("SendFailed", "Send")]
        [TestCase("ReadFailed", "Read")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RealWorkerRecordsTransportFailureAndPhase(string failure, string phase)
        {
            WorkerHealthVerb verb = new WorkerHealthVerb();
            verb.Pipe.ConnectError = failure == "ConnectTimeout" ? new TimeoutException("private")
                : failure == "ConnectFailed" ? new IOException("private") : null;
            verb.Pipe.SendError = failure == "SendFailed" ? new IOException("private") : null;
            verb.Pipe.ReadError = failure == "ReadFailed" ? new IOException("private") : null;
            HealthVerb.CacheLookupResult result = verb.TryGetCachedHydrationStatus(null);
            Assert.AreEqual(failure.EndsWith("Failed") && phase != "Connect" ? "Failed" : failure, result.Outcome.ToString());
            Assert.AreEqual(phase, result.Phase.ToString());
            Assert.AreEqual(500, verb.Pipe.ConnectTimeoutMs);
            Assert.IsTrue(verb.Pipe.Disposed);
            Assert.That(EventMetadataConverter.SerializeToString(result.GetMetadata()), Does.Not.Contain("private"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RealPipeConnectionReturnsItsTimeoutCause()
        {
            using (NamedPipeClient client = new NamedPipeClient("GVFS.UnitTests.Missing." + Guid.NewGuid().ToString("N")))
            {
                Assert.IsFalse(client.Connect(25, out Exception error));
                Assert.IsInstanceOf<TimeoutException>(error);
            }
        }

        [TestCase]
        public void LateWorkerDoesNotMutateTimeoutMetadata()
        {
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            {
                WorkerHealthVerb verb = new WorkerHealthVerb();
                verb.Pipe.ReadResponseCallback = () =>
                {
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Test did not release the pipe worker.");
                    }

                    return new NamedPipeMessages.Message("S", "1,2,3,4,100,50");
                };
                try
                {
                    HealthVerb.CacheLookupResult result = verb.TryGetCachedHydrationStatus(null);
                    Assert.AreEqual(HealthVerb.CacheLookupOutcome.Timeout, result.Outcome);
                    Assert.That(result.DurationMs, Is.GreaterThanOrEqualTo(900));
                    string before = EventMetadataConverter.SerializeToString(result.GetMetadata());
                    release.Set();
                    Assert.IsTrue(SpinWait.SpinUntil(() => verb.Pipe.Disposed, TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(before, EventMetadataConverter.SerializeToString(result.GetMetadata()));
                }
                finally
                {
                    release.Set();
                    SpinWait.SpinUntil(() => verb.Pipe.Disposed, TimeSpan.FromSeconds(5));
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalFailureRetainsIdentityWithoutLeakingExceptionText()
        {
            using (JsonTracer tracer = new JsonTracer("test", "health", disableTelemetry: true))
            {
                MetadataListener telemetry = new MetadataListener(Keywords.Telemetry);
                MetadataListener local = new MetadataListener(Keywords.Any);
                tracer.AddEventListener(telemetry);
                tracer.AddEventListener(local);
                ThrowingHealthVerb verb = new ThrowingHealthVerb();
                Assert.Throws<IOException>(() => verb.ExecuteWithCompletion(null, tracer, Guid.NewGuid()));
                string completion = telemetry.Events.Single(e => e.EventName == "health").Payload;
                Assert.That(completion, Does.Contain("\"Outcome\":\"Failed\""));
                Assert.That(completion, Does.Contain("\"ExceptionType\":\"System.IO.IOException\""));
                Assert.That(completion, Does.Not.Contain("private"));
                Assert.That(local.Events.Single(e => e.EventName == "Warning").Payload, Does.Contain("private"));
            }
        }

        private sealed class WorkerHealthVerb : HealthVerb
        {
            public MockNamedPipeClient Pipe { get; } = new MockNamedPipeClient();

            internal override NamedPipeClient CreateHydrationPipeClient(GVFSEnlistment enlistment)
            {
                return this.Pipe;
            }
        }

        private sealed class ThrowingHealthVerb : HealthVerb
        {
            internal override void ExecuteWithTracer(GVFSEnlistment enlistment, ITracer tracer, EventMetadata metadata, Guid commandId)
            {
                throw new IOException("mock:\\private\\details");
            }
        }

        private sealed class MetadataListener : EventListener
        {
            public MetadataListener(Keywords keywords) : base(EventLevel.Verbose, keywords, null)
            {
            }

            public List<TraceEventMessage> Events { get; } = new List<TraceEventMessage>();

            protected override void RecordMessageInternal(TraceEventMessage message)
            {
                this.Events.Add(message);
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
