using GVFS.CommandLine;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class PrefetchVerbTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClonePrefetchContextIsOptIn(bool fromClone, bool prefetchOffloadEnabled)
        {
            const string EnlistmentRoot = @"mock:\repo with spaces";
            const string LogFilePath = @"mock:\repo with spaces\.gvfs\logs\gvfs_prefetch.log";
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                PrefetchVerb verb = new PrefetchVerb { FromClone = fromClone, Output = output };
                string title = "Original title";
                verb.WriteClonePrefetchContext(tracer, EnlistmentRoot, LogFilePath, prefetchOffloadEnabled, value => title = value);
                Assert.That(tracer.RelatedWarningEvents, Is.Empty);
                if (fromClone)
                {
                    Assert.That(title, Is.EqualTo("VFS for Git: history prefetch - " + EnlistmentRoot));
                    Assert.That(output.ToString(), Does.Contain("Repository: " + EnlistmentRoot));
                    Assert.That(output.ToString(), Does.Contain("Prefetch client process ID: " + Environment.ProcessId));
                    Assert.That(output.ToString(), Does.Contain("builds local pack indexes"));
                    Assert.That(output.ToString(), Does.Contain("After mount succeeds, you can use the repository while prefetch runs."));
                    Assert.That(output.ToString(), Does.Contain("gvfs prefetch --commits"));
                    Assert.That(output.ToString(), Does.Contain("Log: " + LogFilePath));
                    Assert.That(
                        output.ToString().Contains("If the mount performs this prefetch, closing this window does not stop that work."),
                        Is.EqualTo(prefetchOffloadEnabled));
                }
                else
                {
                    Assert.That(output.ToString(), Is.Empty);
                    Assert.That(title, Is.EqualTo("Original title"));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClonePrefetchContextContinuesAfterTitleFailure(bool accessDenied)
        {
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                PrefetchVerb verb = new PrefetchVerb { FromClone = true, Output = output };
                Exception failure = accessDenied
                    ? new UnauthorizedAccessException("Console title access denied")
                    : new IOException("Console title unavailable");

                verb.WriteClonePrefetchContext(
                    tracer,
                    @"mock:\repo",
                    @"mock:\repo\.gvfs\logs\gvfs_prefetch.log",
                    prefetchOffloadEnabled: false,
                    _ => throw failure);

                Assert.That(tracer.RelatedWarningEvents, Has.Count.EqualTo(1));
                Assert.That(tracer.RelatedWarningEvents[0], Does.Contain(failure.GetType().FullName));
                Assert.That(tracer.RelatedWarningEvents[0], Does.Contain(failure.Message));
                Assert.That(tracer.RelatedWarningEvents[0], Does.Contain(nameof(PrefetchVerb.WriteClonePrefetchContext)));
                Assert.That(output.ToString(), Does.Contain(@"Repository: mock:\repo"));
                Assert.That(output.ToString(), Does.Contain("gvfs prefetch --commits"));
            }
        }
    }
}
