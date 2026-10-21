using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    // The mount process passes its tracer to every config reader so that retries are traced.
    [TestFixture]
    public class ConfigReaderTracerTests
    {
        [TestCase]
        public void CacheServerResolverCreatesItsGitProcessWithTheTracer()
        {
            MockTracer tracer = new MockTracer();
            TracerCapturingEnlistment enlistment = this.CreateEnlistment();

            CacheServerResolver.GetCacheServerFromConfig(enlistment, tracer);

            (enlistment.CapturedTracers.Count > 0).ShouldBeTrue();
            foreach (ITracer capturedTracer in enlistment.CapturedTracers)
            {
                capturedTracer.ShouldBeSameAs(tracer);
            }
        }

        [TestCase]
        public void RetryConfigCreatesItsGitProcessWithTheTracerWhenTheEnvironmentChangesGitConfig()
        {
            MockTracer tracer = new MockTracer();
            TracerCapturingEnlistment enlistment = this.CreateEnlistment();

            RetryConfig.TryLoadFromGitConfig(
                tracer,
                RetryConfig.CreateConfigReader(tracer, enlistment, new Hashtable { { "GIT_CONFIG_COUNT", "1" } }),
                out RetryConfig _,
                out string _);

            // One git.exe process reads both settings.
            enlistment.CapturedTracers.Count.ShouldEqual(1);
            enlistment.CapturedTracers[0].ShouldBeSameAs(tracer);
        }

        [TestCase]
        public void RetryConfigDoesNotStartGitWhenLibGit2ReadsTheSettings()
        {
            TracerCapturingEnlistment enlistment = this.CreateEnlistment();

            // The mock working directory does not exist, so libgit2 reads the empty isolated global config.
            using (new IsolatedLibGit2Config())
            {
                RetryConfig.TryLoadFromGitConfig(
                    new MockTracer(),
                    RetryConfig.CreateConfigReader(new MockTracer(), enlistment, new Hashtable { { "PATH", "C:\\bin" } }),
                    out RetryConfig _,
                    out string _);
            }

            enlistment.CapturedTracers.Count.ShouldEqual(0);
        }

        [TestCase]
        public void GitStatusCacheConfigCreatesItsGitProcessWithTheTracer()
        {
            MockTracer tracer = new MockTracer();
            TracerCapturingEnlistment enlistment = this.CreateEnlistment();

            GitStatusCacheConfig.TryLoadFromGitConfig(tracer, enlistment, out GitStatusCacheConfig _, out string _);

            enlistment.CapturedTracers.Count.ShouldEqual(1);
            enlistment.CapturedTracers[0].ShouldBeSameAs(tracer);
        }

        private TracerCapturingEnlistment CreateEnlistment()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                "config",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode),
                matchPrefix: true);
            return new TracerCapturingEnlistment(git);
        }

        private class TracerCapturingEnlistment : MockGVFSEnlistment
        {
            public TracerCapturingEnlistment(MockGitProcess gitProcess)
                : base(gitProcess)
            {
            }

            public List<ITracer> CapturedTracers { get; } = new List<ITracer>();

            public override GitProcess CreateGitProcess(ITracer tracer)
            {
                this.CapturedTracers.Add(tracer);
                return base.CreateGitProcess(tracer);
            }
        }
    }
}
