using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Service
{
    [TestFixture]
    public class InstallationTelemetryTests
    {
        private string directory;
        private InstallationTelemetryStore store;
        private MockFileSystem fileSystem;
        private TestSecurity security;

        [SetUp]
        public void SetUp()
        {
            this.directory = @"C:\MockInstallationTelemetry";
            this.fileSystem = new MockFileSystem(new MockDirectory(this.directory, null, null));
            this.fileSystem.DeleteNonExistentFileThrowsException = false;
            this.security = new TestSecurity();
            this.store = new InstallationTelemetryStore(
                this.directory, NullTracer.Instance, fileSystem: this.fileSystem, security: this.security);
        }

        [Test]
        [Category("ExceptionExpected")]
        public void ReaderIgnoresUnpublishedAndUnsupportedRecordsWithoutBlockingValidRecords()
        {
            InstallationTelemetryRecord valid = CreateRecord();
            this.Write(valid);
            string unpublished = this.WriteRaw(Guid.NewGuid(), "{partial", ".tmp");
            string malformed = this.WriteRaw(Guid.NewGuid(), "{malformed");
            InstallationTelemetryRecord future = CreateRecord();
            future.SchemaVersion = 2;
            string futurePath = this.Write(future);
            List<KeyValuePair<string, InstallationTelemetryRecord>> batch = this.store.ReadBatch();
            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual(valid.EventId, batch[0].Value.EventId);
            CollectionAssert.AreEquivalent(
                new string[] { unpublished, malformed, futurePath, Path.Combine(this.directory, valid.EventId + ".json") },
                this.fileSystem.GetFiles(this.directory, "*"));
        }

        [Test]
        public void ReaderExpiresEventsByOccurrenceAndEnforcesCapacityBeforeSending()
        {
            InstallationTelemetryRecord expired = CreateRecord();
            expired.OccurredUtc = DateTimeOffset.UtcNow.AddDays(-8);
            string expiredPath = this.Write(expired);
            Assert.IsEmpty(this.store.ReadBatch());
            Assert.IsFalse(this.fileSystem.FileExists(expiredPath));
            for (int i = 0; i < InstallationTelemetryStore.MaxRecords + 1; i++)
            {
                this.Write(CreateRecord());
            }

            Assert.AreEqual(InstallationTelemetryStore.MaxRecords, this.store.ReadBatch().Count);
            Assert.AreEqual(InstallationTelemetryStore.MaxRecords, this.fileSystem.GetFiles(this.directory, "*").Length);
        }

        [Test]
        [Category("ExceptionExpected")]
        public void ReaderRejectsOversizeRecordsInvalidFieldsAndMismatchedIdentity()
        {
            string oversized = this.WriteRaw(Guid.NewGuid(), new string('x', 5000));
            InstallationTelemetryRecord invalid = CreateRecord();
            invalid.Outcome = @"private:\unapproved";
            string invalidPath = this.Write(invalid);
            InstallationTelemetryRecord mismatch = CreateRecord();
            string mismatchPath = this.WriteRaw(Guid.NewGuid(), JsonSerializer.Serialize(mismatch));
            Assert.IsEmpty(this.store.ReadBatch());
            Assert.IsFalse(this.fileSystem.FileExists(oversized));
            CollectionAssert.AreEquivalent(new string[] { invalidPath, mismatchPath }, this.fileSystem.GetFiles(this.directory, "*"));
        }

        [Test]
        [Category("ExceptionExpected")]
        public void ReaderRetainsRecordsWhenDirectorySecurityFails()
        {
            string path = this.Write(CreateRecord());
            this.security.RejectDirectory = true;
            Assert.IsEmpty(this.store.ReadBatch());
            Assert.IsTrue(this.fileSystem.FileExists(path));
        }

        [Test]
        [Category("ExceptionExpected")]
        public void ReaderRejectsOnlyTheUntrustedFile()
        {
            string path = this.Write(CreateRecord());
            this.security.RejectedFile = path;
            InstallationTelemetryRecord valid = CreateRecord();
            this.Write(valid);
            Assert.AreEqual(valid.EventId, this.store.ReadBatch().Single().Value.EventId);
            Assert.IsTrue(this.fileSystem.FileExists(path));
        }

        [Test]
        public void ProducerDescriptorPermitsReadOnlyUsers()
        {
            new InstallationTelemetrySecurity().VerifyDescriptor(
                new RawSecurityDescriptor("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)"),
                ancestorsOnly: false);
        }

        [TestCase("O:BUD:P(A;;FA;;;SY)", false)]
        [TestCase("O:BAD:P(A;;GW;;;BU)", false)]
        [TestCase("O:BAD:P(A;;GA;;;BU)", true)]
        [TestCase("O:BAD:P(A;;0x00000040;;;BU)", true)]
        [TestCase("O:BAD:P(A;;WD;;;BU)", true)]
        [TestCase("O:BAD:P(OA;;GW;00000000-0000-0000-0000-000000000001;;BU)", false)]
        [Category("ExceptionExpected")]
        public void DescriptorRejectsUntrustedOwnersGenericWritesAndUnsupportedAces(string descriptor, bool ancestorsOnly)
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                new InstallationTelemetrySecurity().VerifyDescriptor(new RawSecurityDescriptor(descriptor), ancestorsOnly));
        }

        [Test]
        public void OversizeNewestFileDoesNotEvictValidOlderRecords()
        {
            for (int i = 0; i < InstallationTelemetryStore.MaxRecords; i++)
            {
                this.Write(CreateRecord());
            }

            string oversized = this.WriteRaw(Guid.NewGuid(), new string('x', InstallationTelemetryStore.MaxTotalBytes + 1));
            Assert.AreEqual(InstallationTelemetryStore.MaxRecords, this.store.ReadBatch().Count);
            Assert.IsFalse(this.fileSystem.FileExists(oversized));
            Assert.AreEqual(InstallationTelemetryStore.MaxRecords, this.fileSystem.GetFiles(this.directory, "*").Length);
        }

        [TestCase("Started", "Fresh", "Prepare", "NotRequested", 0)]
        [TestCase("Completed", "Immediate", "Done", "Succeeded", 0)]
        [TestCase("Completed", "Immediate", "Done", "Failed", 17)]
        [TestCase("Staged", "Staged", "Done", "NotRequested", 0)]
        [TestCase("Cancelled", "Fresh", "Prepare", "NotRequested", 0)]
        [TestCase("Incomplete", "Fresh", "Install", "NotRequested", 0)]
        [TestCase("Failed", "Immediate", "Service", "NotRequested", 0)]
        public async Task ProducerShapedJsonReachesThePipe(string outcome, string mode, string phase, string remount, int result)
        {
            Guid eventId = Guid.NewGuid();
            Guid attemptId = Guid.NewGuid();
            string stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            string eventType = outcome == "Started" ? "InstallationStarted" : "InstallationFinished";
            string json = "{\"SchemaVersion\":1,\"AttemptId\":\"" + attemptId + "\",\"EventId\":\"" + eventId +
                "\",\"EventType\":\"" + eventType + "\",\"OccurredUtc\":\"" + stamp +
                "\",\"InstallerVersion\":\"0.2.1.0\",\"PreviousVersion\":\"Unknown\",\"PackageArchitecture\":\"arm64\",\"OSArchitecture\":\"arm64\"," +
                "\"Mode\":\"" + mode + "\",\"Outcome\":\"" + outcome + "\",\"LastPhase\":\"" + phase +
                "\",\"RemountOutcome\":\"" + remount + "\",\"ScriptResult\":" + result + "}";
            string path = this.WriteRaw(eventId, json);
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = CreateServer(pipeName))
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            {
                Task<string> received = ReadMessage(server, timeout.Token);
                Assert.AreEqual(1, await monitor.DrainAsync(timeout.Token));
                using (JsonDocument envelope = JsonDocument.Parse(await received))
                using (JsonDocument payload = JsonDocument.Parse(envelope.RootElement.GetProperty("payload").GetProperty("json").GetString()))
                {
                    Assert.AreEqual(eventType, envelope.RootElement.GetProperty("eventName").GetString());
                    Assert.AreEqual(eventId, payload.RootElement.GetProperty("EventId").GetGuid());
                    Assert.AreEqual(attemptId, payload.RootElement.GetProperty("AttemptId").GetGuid());
                    Assert.AreEqual(DateTimeOffset.Parse(stamp), payload.RootElement.GetProperty("OccurredUtc").GetDateTimeOffset());
                    Assert.AreEqual(outcome, payload.RootElement.GetProperty("Outcome").GetString());
                    Assert.AreEqual("arm64", payload.RootElement.GetProperty("PackageArchitecture").GetString());
                    Assert.AreEqual(result, payload.RootElement.GetProperty("ScriptResult").GetInt32());
                }

                Assert.IsFalse(this.fileSystem.FileExists(path));
            }
        }

        [TestCase("anonymous")]
        [TestCase("ANONYMOUS")]
        [TestCase("invalid:name")]
        [Category("ExceptionExpected")]
        public void InvalidPipeNamesRetainRecords(string name)
        {
            string path = this.Write(CreateRecord());
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => name))
            {
                Assert.ThrowsAsync<InvalidDataException>(() => monitor.DrainAsync(CancellationToken.None));
                Assert.IsTrue(this.fileSystem.FileExists(path));
            }
        }

        [Test]
        [Category("ExceptionExpected")]
        public async Task PartialBatchDeletesOnlyTheSuccessfulRecordAndRetriesTheOther()
        {
            InstallationTelemetryRecord first = CreateRecord();
            string firstPath = this.Write(first);
            this.fileSystem.RootDirectory.FindFile(firstPath).FileProperties = new FileProperties(
                FileAttributes.Normal, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-1), 0);
            InstallationTelemetryRecord second = CreateRecord();
            string secondPath = this.Write(second);
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                using (NamedPipeServerStream server = CreateServer(pipeName))
                {
                    Task<string> received = ReadMessage(server, timeout.Token);
                    Assert.CatchAsync<OperationCanceledException>(() => monitor.DrainAsync(timeout.Token));
                    using (JsonDocument message = JsonDocument.Parse(await received))
                    using (JsonDocument payload = JsonDocument.Parse(message.RootElement.GetProperty("payload").GetProperty("json").GetString()))
                    {
                        Assert.AreEqual(first.EventId, payload.RootElement.GetProperty("EventId").GetGuid());
                    }
                }

                Assert.IsFalse(this.fileSystem.FileExists(firstPath));
                Assert.IsTrue(this.fileSystem.FileExists(secondPath));
                using (NamedPipeServerStream server = CreateServer(pipeName))
                {
                    Task<string> received = ReadMessage(server, timeout.Token);
                    Assert.AreEqual(1, await monitor.DrainAsync(timeout.Token));
                    using (JsonDocument message = JsonDocument.Parse(await received))
                    using (JsonDocument payload = JsonDocument.Parse(message.RootElement.GetProperty("payload").GetProperty("json").GetString()))
                    {
                        Assert.AreEqual(second.EventId, payload.RootElement.GetProperty("EventId").GetGuid());
                    }
                }

                Assert.IsEmpty(this.fileSystem.GetFiles(this.directory, "*"));
            }
        }

        [Test]
        public async Task BatchStopsAfterSixteenSuccessfulRecords()
        {
            for (int i = 0; i < 17; i++)
            {
                this.Write(CreateRecord());
            }

            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            {
                Task<string[]> received = ReadMessages(pipeName, 16, timeout.Token);
                Assert.AreEqual(16, await monitor.DrainAsync(timeout.Token));
                Assert.AreEqual(16, (await received).Length);
                Assert.AreEqual(1, this.fileSystem.GetFiles(this.directory, "*").Length);
            }
        }

        [Test]
        [Category("ExceptionExpected")]
        public async Task WakeDeliversPendingRecordsAfterAnAttachmentAppears()
        {
            string path = this.Write(CreateRecord());
            string destination = null;
            using (ManualResetEventSlim resolved = new ManualResetEventSlim())
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() =>
            {
                string selected = Volatile.Read(ref destination);
                resolved.Set();
                return selected;
            }))
            {
                monitor.Start();
                Assert.IsTrue(resolved.Wait(TimeSpan.FromSeconds(5)));
                string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
                using (NamedPipeServerStream server = CreateServer(pipeName))
                {
                    Task<string> received = ReadMessage(server, timeout.Token);
                    Volatile.Write(ref destination, pipeName);
                    monitor.Wake();
                    await received;
                    Assert.IsTrue(SpinWait.SpinUntil(() => !this.fileSystem.FileExists(path), TimeSpan.FromSeconds(5)));
                }
            }
        }

        [Test]
        public void HealthReportsOnlyRateLimitedSanitizedFailures()
        {
            MockTracer tracer = new MockTracer();
            TestClock clock = new TestClock();
            InstallationTelemetryHealth health = new InstallationTelemetryHealth(tracer, clock);
            health.Flush();
            Assert.IsEmpty(tracer.RelatedEventNames);
            health.ReportFailure(InstallationTelemetryHealth.FailureReason.StoreRejected, @"private:\path");
            health.ReportFailure(InstallationTelemetryHealth.FailureReason.StoreRejected, @"private:\path");
            Assert.AreEqual(1, tracer.RelatedEventNames.Count);
            Assert.AreEqual("InstallationTelemetryFailure", tracer.RelatedEventNames[0]);
            Assert.AreEqual(Keywords.Telemetry, tracer.RelatedEventKeywords[0]);
            Assert.AreEqual("StoreRejected", tracer.RelatedEventMetadata[0]["Reason"]);
            Assert.AreEqual(1L, tracer.RelatedEventMetadata[0]["FailureCount"]);
            Assert.That(tracer.RelatedEventMetadataSerialized[0], Does.Not.Contain("private"));
            clock.Now = clock.Now.AddMinutes(5);
            health.Flush();
            Assert.AreEqual(2, tracer.RelatedEventNames.Count);
            Assert.AreEqual("StoreRejected", tracer.RelatedEventMetadata[1]["Reason"]);
            Assert.AreEqual(1L, tracer.RelatedEventMetadata[1]["FailureCount"]);
            Assert.That(tracer.RelatedEventMetadataSerialized[1], Does.Not.Contain("private"));
            health.Flush();
            Assert.AreEqual(2, tracer.RelatedEventNames.Count);
            CollectionAssert.AreEqual(new Keywords[] { Keywords.None }, tracer.RelatedWarningKeywords);
        }

        [Test]
        public void HealthKeepsReasonCountsSeparateAndDoesNotSuppressTerminalFaults()
        {
            MockTracer tracer = new MockTracer();
            TestClock clock = new TestClock();
            InstallationTelemetryHealth health = new InstallationTelemetryHealth(tracer, clock);
            for (int i = 0; i < 10; i++)
            {
                health.ReportFailure(InstallationTelemetryHealth.FailureReason.StoreRejected, "local-detail");
            }

            health.ReportFailure(InstallationTelemetryHealth.FailureReason.DeliveryFailed, "local-detail");
            health.ReportFailure(InstallationTelemetryHealth.FailureReason.WorkerStopped, "local-detail");
            Assert.AreEqual(3, tracer.RelatedEventNames.Count);
            CollectionAssert.AreEqual(
                new string[] { "StoreRejected", "DeliveryFailed", "WorkerStopped" },
                tracer.RelatedEventMetadata.Select(metadata => (string)metadata["Reason"]).ToArray());
            CollectionAssert.AreEqual(
                new long[] { 1, 1, 1 },
                tracer.RelatedEventMetadata.Select(metadata => (long)metadata["FailureCount"]).ToArray());
            clock.Now = clock.Now.AddMinutes(5);
            health.Flush();
            Assert.AreEqual(4, tracer.RelatedEventNames.Count);
            Assert.AreEqual("StoreRejected", tracer.RelatedEventMetadata[3]["Reason"]);
            Assert.AreEqual(9L, tracer.RelatedEventMetadata[3]["FailureCount"]);
        }

        [Test]
        [Category("ExceptionExpected")]
        public async Task HealthFailureReachesTheExistingPipeWithoutLocalDetails()
        {
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (JsonTracer tracer = new JsonTracer("Test", "Test", disableTelemetry: true))
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = CreateServer(pipeName))
            {
                tracer.AddEventListener(new TelemetryDaemonEventListener(
                    "Test", null, null, pipeName, tracer));
                Task<string> received = ReadMessage(server, timeout.Token);
                InstallationTelemetryHealth health = new InstallationTelemetryHealth(tracer, TimeProvider.System);
                health.ReportFailure(InstallationTelemetryHealth.FailureReason.StoreRejected, @"private:\path");
                using (JsonDocument message = JsonDocument.Parse(await received))
                using (JsonDocument payload = JsonDocument.Parse(message.RootElement.GetProperty("payload").GetProperty("json").GetString()))
                {
                    Assert.AreEqual("InstallationTelemetryFailure", message.RootElement.GetProperty("eventName").GetString());
                    Assert.AreEqual("StoreRejected", payload.RootElement.GetProperty("Reason").GetString());
                    Assert.AreEqual(1, payload.RootElement.GetProperty("FailureCount").GetInt32());
                    Assert.That(message.RootElement.GetRawText(), Does.Not.Contain("private"));
                }
            }
        }

        [Test]
        [Category("ExceptionExpected")]
        public async Task MissingCollectorRetainsRecordAcrossMonitorRestart()
        {
            InstallationTelemetryRecord record = CreateRecord();
            string path = this.Write(record);
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await monitor.DrainAsync(CancellationToken.None));
            }

            Assert.IsTrue(this.fileSystem.FileExists(path));
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = CreateServer(pipeName))
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            {
                Task<string> received = ReadMessage(server, timeout.Token);
                Assert.AreEqual(1, await monitor.DrainAsync(timeout.Token));
                TelemetryDaemonEventListener.PipeMessage message = TelemetryDaemonEventListener.PipeMessage.FromJson(await received);
                InstallationTelemetryRecord payload = JsonSerializer.Deserialize<InstallationTelemetryRecord>(message.Payload.Json);
                Assert.AreEqual(record.EventId, payload.EventId);
                Assert.AreEqual(record.OccurredUtc, payload.OccurredUtc);
                Assert.AreEqual(record.InstallerVersion, payload.InstallerVersion);
                Assert.AreEqual("InstallationStarted", message.EventName);
                Assert.IsFalse(this.fileSystem.FileExists(path));
            }
        }

        [Test]
        public async Task ConsecutiveWritesCompleteOnlyTheirOwnRecordsAndStripUnknownFields()
        {
            InstallationTelemetryRecord first = CreateRecord();
            string firstPath = this.Write(first);
            string original = this.fileSystem.ReadAllText(firstPath);
            this.WriteRaw(first.EventId, original.Substring(0, original.Length - 1) + ",\"PrivateDetail\":\"must-not-leave-disk\"}");
            InstallationTelemetryRecord second = CreateRecord();
            this.Write(second);
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => pipeName))
            {
                Task<string[]> received = ReadMessages(pipeName, 2, timeout.Token);
                Assert.AreEqual(2, await monitor.DrainAsync(timeout.Token));
                string[] messages = await received;
                Assert.That(string.Join("", messages), Does.Not.Contain("must-not-leave-disk"));
                Guid[] ids = messages.Select(json => JsonSerializer.Deserialize<InstallationTelemetryRecord>(
                    TelemetryDaemonEventListener.PipeMessage.FromJson(json).Payload.Json).EventId).ToArray();
                CollectionAssert.AreEquivalent(new Guid[] { first.EventId, second.EventId }, ids);
                Assert.IsEmpty(this.store.ReadBatch());
            }
        }

        [Test]
        public async Task DeliveryWaitsForAnAttachmentAndReadsItsSelectedPipe()
        {
            string destination = string.Empty;
            using (InstallationTelemetryMonitor monitor = this.CreateMonitor(() => destination))
            {
                string firstPath = this.Write(CreateRecord());
                Assert.AreEqual(0, await monitor.DrainAsync(CancellationToken.None));
                Assert.IsTrue(this.fileSystem.FileExists(firstPath));
                for (int i = 0; i < 2; i++)
                {
                    destination = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    using (NamedPipeServerStream server = CreateServer(destination))
                    {
                        Task<string> received = ReadMessage(server, timeout.Token);
                        Assert.AreEqual(1, await monitor.DrainAsync(timeout.Token));
                        await received;
                    }

                    this.Write(CreateRecord());
                }

                destination = string.Empty;
                Assert.AreEqual(0, await monitor.DrainAsync(CancellationToken.None));
                Assert.AreEqual(1, this.store.ReadBatch().Count);
            }
        }

        [Test]
        [Category("ExceptionExpected")]
        public async Task StalledReaderCannotBlockTheSendPastItsDeadline()
        {
            string pipeName = "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = new NamedPipeServerStream(
                pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024, 1024))
            {
                Task connected = server.WaitForConnectionAsync(timeout.Token);
                Task send = InstallationTelemetryMonitor.SendAsync(pipeName, new string('x', 1024 * 1024), timeout.Token);
                await connected;
                Assert.CatchAsync<OperationCanceledException>(async () => await send);
                Assert.IsFalse(timeout.IsCancellationRequested);
            }
        }

        [Test]
        [Category("ExceptionExpected")]
        public void WorkerStopCancelsAnUnavailablePipe()
        {
            this.Write(CreateRecord());
            using (ManualResetEventSlim resolving = new ManualResetEventSlim())
            {
                InstallationTelemetryMonitor monitor = new InstallationTelemetryMonitor(this.store, NullTracer.Instance, () =>
                {
                    resolving.Set();
                    return "GVFS.InstallationTests." + Guid.NewGuid().ToString("N");
                });
                monitor.Start();
                Assert.IsTrue(resolving.Wait(TimeSpan.FromSeconds(10)));
                System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
                monitor.Dispose();
                Assert.Less(timer.Elapsed, TimeSpan.FromSeconds(2));
                Assert.AreEqual(1, this.store.ReadBatch().Count);
            }
        }

        [Test]
        public void ConfigurationPrecedenceKeepsSystemAndFallsBackForWhitespace()
        {
            Assert.AreEqual("system", TelemetryDaemonEventListener.ResolvePipeName(() => "system\r\n", () => throw new Exception("Unexpected fallback")));
            Assert.AreEqual("global", TelemetryDaemonEventListener.ResolvePipeName(() => " \r\n", () => "global\n"));
        }

        private static InstallationTelemetryRecord CreateRecord()
        {
            return new InstallationTelemetryRecord
            {
                SchemaVersion = 1,
                AttemptId = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                EventType = "InstallationStarted",
                OccurredUtc = DateTimeOffset.UtcNow,
                InstallerVersion = "0.2.1.0",
                PreviousVersion = "Unknown",
                PackageArchitecture = "x64",
                OSArchitecture = "arm64",
                Mode = "Fresh",
                Outcome = "Started",
                LastPhase = "Prepare",
                RemountOutcome = "NotRequested",
            };
        }

        private static NamedPipeServerStream CreateServer(string name)
        {
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        private static async Task<string> ReadMessage(NamedPipeServerStream server, CancellationToken token)
        {
            await server.WaitForConnectionAsync(token);
            using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
            {
                return await reader.ReadLineAsync(token);
            }
        }

        private static async Task<string[]> ReadMessages(string name, int count, CancellationToken token)
        {
            List<string> messages = new List<string>();
            for (int i = 0; i < count; i++)
            {
                using (NamedPipeServerStream server = CreateServer(name))
                {
                    messages.Add(await ReadMessage(server, token));
                }
            }

            return messages.ToArray();
        }

        private string Write(InstallationTelemetryRecord record)
        {
            return this.WriteRaw(record.EventId, JsonSerializer.Serialize(record));
        }

        private string WriteRaw(Guid id, string json, string extension = ".json")
        {
            string path = Path.Combine(this.directory, id.ToString("D") + extension);
            this.fileSystem.WriteAllText(path, json);
            MockFile file = this.fileSystem.RootDirectory.FindFile(path);
            file.FileProperties = new FileProperties(FileAttributes.Normal, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, json.Length);
            return path;
        }

        private InstallationTelemetryMonitor CreateMonitor(Func<string> resolve)
        {
            return new InstallationTelemetryMonitor(this.store, NullTracer.Instance, resolve);
        }

        private class TestSecurity : InstallationTelemetrySecurity
        {
            public bool RejectDirectory { get; set; }
            public string RejectedFile { get; set; }

            public override void VerifyDirectory(string path)
            {
                if (this.RejectDirectory)
                {
                    throw new UnauthorizedAccessException("Synthetic directory rejection.");
                }
            }

            public override void VerifyFile(string path)
            {
                if (path == this.RejectedFile)
                {
                    throw new UnauthorizedAccessException("Synthetic file rejection.");
                }
            }
        }

        private class TestClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

            public override DateTimeOffset GetUtcNow()
            {
                return this.Now;
            }
        }
    }
}
