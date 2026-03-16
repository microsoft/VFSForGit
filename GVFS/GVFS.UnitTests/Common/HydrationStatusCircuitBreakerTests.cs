using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class HydrationStatusCircuitBreakerTests
    {
        private MockTracer tracer;
        private string dotGVFSRoot;
        private string tempDir;

        [SetUp]
        public void Setup()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), "GVFS_CircuitBreakerTest_" + Path.GetRandomFileName());
            this.dotGVFSRoot = Path.Combine(this.tempDir, ".gvfs");
            Directory.CreateDirectory(Path.Combine(this.dotGVFSRoot, "gitStatusCache"));
            this.tracer = new MockTracer();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, recursive: true);
            }
        }

        [Test]
        public void IsDisabledReturnsFalseWhenNoMarkerFile()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();
            breaker.IsDisabled().ShouldBeFalse();
        }

        [Test]
        public void SingleFailureDoesNotDisable()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();
            breaker.RecordFailure();
            breaker.IsDisabled().ShouldBeFalse();
        }

        [Test]
        public void TwoFailuresDoNotDisable()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();
            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.IsDisabled().ShouldBeFalse();
        }

        [Test]
        public void ThreeFailuresTripsBreaker()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();
            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.IsDisabled().ShouldBeTrue();
        }

        [Test]
        public void BreakerResetsOnNewDay()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();

            // Simulate a marker file from yesterday
            string markerPath = Path.Combine(
                this.dotGVFSRoot,
                GVFSConstants.DotGVFS.HydrationStatus.DisabledMarkerFile);
            File.WriteAllText(
                markerPath,
                $"2020-01-01\n{ProcessHelper.GetCurrentProcessVersion()}\n5");

            breaker.IsDisabled().ShouldBeFalse("Circuit breaker should reset on a new day");
        }

        [Test]
        public void BreakerResetsOnVersionChange()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();

            // Simulate a marker file with a different GVFS version
            string markerPath = Path.Combine(
                this.dotGVFSRoot,
                GVFSConstants.DotGVFS.HydrationStatus.DisabledMarkerFile);
            string today = System.DateTime.UtcNow.ToString("yyyy-MM-dd");
            File.WriteAllText(
                markerPath,
                $"{today}\n99.99.99.99\n5");

            breaker.IsDisabled().ShouldBeFalse("Circuit breaker should reset when GVFS version changes");
        }

        [Test]
        public void BreakerStaysTrippedOnSameDayAndVersion()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();

            string markerPath = Path.Combine(
                this.dotGVFSRoot,
                GVFSConstants.DotGVFS.HydrationStatus.DisabledMarkerFile);
            string today = System.DateTime.UtcNow.ToString("yyyy-MM-dd");
            string currentVersion = ProcessHelper.GetCurrentProcessVersion();
            File.WriteAllText(
                markerPath,
                $"{today}\n{currentVersion}\n3");

            breaker.IsDisabled().ShouldBeTrue("Circuit breaker should remain tripped on same day and version");
        }

        [Test]
        public void TryParseMarkerFileHandlesValidContent()
        {
            bool result = HydrationStatusCircuitBreaker.TryParseMarkerFile(
                "2026-03-11\n0.2.26070.19566\n3",
                out string date,
                out string version,
                out int count);

            result.ShouldBeTrue();
            date.ShouldEqual("2026-03-11");
            version.ShouldEqual("0.2.26070.19566");
            count.ShouldEqual(3);
        }

        [Test]
        public void TryParseMarkerFileHandlesEmptyContent()
        {
            HydrationStatusCircuitBreaker.TryParseMarkerFile(
                string.Empty,
                out string _,
                out string _,
                out int _).ShouldBeFalse();
        }

        [Test]
        public void TryParseMarkerFileHandlesCorruptContent()
        {
            HydrationStatusCircuitBreaker.TryParseMarkerFile(
                "garbage",
                out string _,
                out string _,
                out int _).ShouldBeFalse();
        }

        [Test]
        public void TryParseMarkerFileHandlesNonNumericCount()
        {
            HydrationStatusCircuitBreaker.TryParseMarkerFile(
                "2026-03-11\n0.2.26070.19566\nabc",
                out string _,
                out string _,
                out int _).ShouldBeFalse();
        }

        [Test]
        public void RecordFailureLogsWarningWhenBreakerTrips()
        {
            HydrationStatusCircuitBreaker breaker = this.CreateBreaker();
            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.RecordFailure();

            this.tracer.RelatedWarningEvents.Count.ShouldBeAtLeast(
                1,
                "Should log a warning when circuit breaker trips");
        }

        private HydrationStatusCircuitBreaker CreateBreaker()
        {
            return new HydrationStatusCircuitBreaker(
                this.dotGVFSRoot,
                this.tracer);
        }
    }
}
