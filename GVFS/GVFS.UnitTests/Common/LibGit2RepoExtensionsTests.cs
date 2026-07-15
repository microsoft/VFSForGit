using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoExtensionsTests
    {
        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsConfiguredValue()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(true))
            {
                bool value = repo.GetConfigBoolOrDefault(tracer, "gvfs.test", false);

                value.ShouldEqual(true);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultOnConfigFailure()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(new LibGit2Exception("boom")))
            {
                bool value = repo.GetConfigBoolOrDefault(tracer, "gvfs.test", false);

                value.ShouldEqual(false);
                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("Failed to read gvfs.test config, using default: boom");
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnPathReturnsDefaultForMissingRepo()
        {
            MockTracer tracer = new MockTracer();

            bool value = LibGit2RepoExtensions.GetConfigBoolOrDefault(
                tracer,
                Path.Combine("Z:\\", "path", "that", "does", "not", "exist"),
                "gvfs.test",
                true);

            value.ShouldEqual(true);
            Assert.That(tracer.RelatedWarningEvents.Count, Is.GreaterThanOrEqualTo(1));
            tracer.RelatedWarningEvents.ShouldContain(warning => warning.Contains("Failed to read gvfs.test config, using default:"));
        }

        private class MockConfigRepo : LibGit2Repo
        {
            private readonly bool? value;
            private readonly System.Exception exceptionToThrow;

            public MockConfigRepo(bool? value)
                : base()
            {
                this.value = value;
            }

            public MockConfigRepo(System.Exception exceptionToThrow)
                : base()
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
