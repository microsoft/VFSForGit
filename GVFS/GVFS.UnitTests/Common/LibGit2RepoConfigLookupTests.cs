using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoConfigLookupTests
    {
        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsConfiguredValue()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, true))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(true);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultWhenKeyIsUnset()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, (bool?)null))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", true);

                value.ShouldEqual(true);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultOnLibGit2ExceptionAndLogsOnce()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, new LibGit2Exception("boom")))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(false);
                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("Failed to read gvfs.test config, using default: boom");
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultOnInvalidDataExceptionAndLogsOnce()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, new InvalidDataException("corrupt config")))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(false);
                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("Failed to read gvfs.test config, using default: corrupt config");
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnPathReturnsDefaultForMissingRepoAndLogsExactlyOnce()
        {
            MockTracer tracer = new MockTracer();

            // A GUID-suffixed path under the OS temp directory is guaranteed not to exist and
            // does not depend on any particular drive letter being unmapped (unlike a
            // hardcoded "Z:\..." path, which could resolve on a host with that drive mapped).
            string missingRepoPath = Path.Combine(
                Path.GetTempPath(),
                "LibGit2RepoConfigLookupTests_" + Guid.NewGuid().ToString("N"));

            bool value = LibGit2Repo.GetConfigBoolOrDefault(
                tracer,
                missingRepoPath,
                "gvfs.test",
                false);

            value.ShouldEqual(false);

            // The LibGit2Repo constructor logs a RelatedWarning with the native open-failure
            // reason before throwing InvalidDataException; the static helper's catch does not
            // log a second time for that case (see LibGit2Repo.GetConfigBoolOrDefault), so
            // exactly one warning is expected here.
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.RelatedWarningEvents[0].ShouldContain("Couldn't open repo at");
        }

        private class MockConfigRepo : LibGit2Repo
        {
            private readonly bool? value;
            private readonly Exception exceptionToThrow;

            public MockConfigRepo(MockTracer tracer, bool? value)
                : base(tracer)
            {
                this.value = value;
            }

            public MockConfigRepo(MockTracer tracer, Exception exceptionToThrow)
                : base(tracer)
            {
                this.exceptionToThrow = exceptionToThrow;
            }

            public override bool? GetConfigBool(string name)
            {
                if (this.exceptionToThrow != null)
                {
                    throw this.exceptionToThrow;
                }

                return this.value;
            }
        }
    }
}
