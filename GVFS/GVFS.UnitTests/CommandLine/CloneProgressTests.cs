using GVFS.CommandLine;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CloneProgressTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void NonSpinningStatusKeepsTheBaseMessage(bool success)
        {
            using MockTracer tracer = new MockTracer();
            using StringWriter output = new StringWriter();
            CloneVerb verb = new CloneVerb { Output = output };
            bool result = verb.RunCloneWithProgress(
                tracer,
                @"mock:\repo",
                progress =>
                {
                    progress.StartStage(CloneProgress.Stage.InitializingIndex);
                    return success;
                });

            Assert.That(result, Is.EqualTo(success));
            Assert.That(output.ToString(), Is.EqualTo(
                (success ? "Cloning...Succeeded" : @"Cloning...Failed. Run 'gvfs log mock:\repo' for more info.") + Environment.NewLine));
        }

        [TestCase]
        public void RetryRetainsBothCheckoutAttemptsWithoutATerminalFailure()
        {
            using MockTracer tracer = new MockTracer();
            using (CloneProgress progress = new CloneProgress(tracer))
            {
                progress.StartStage(CloneProgress.Stage.PreparingCommitAndTrees);
                progress.StartStage(CloneProgress.Stage.InitializingIndex);
                progress.StartStage(CloneProgress.Stage.PreparingCommitAndTrees, attempt: 2, retryPreviousStage: true);
                Assert.That(progress.GetMessage(80), Is.EqualTo("Preparing commit and trees (retry)"));
                progress.StartStage(CloneProgress.Stage.InitializingIndex, attempt: 2);
                Assert.That(progress.GetMessage(80), Is.EqualTo("Initializing index (retry)"));
                progress.StartStage(CloneProgress.Stage.FinalizingRepository);
                progress.Complete();
                Assert.That(progress.GetMessage(80), Is.Null);
            }

            Assert.That(tracer.RelatedEventNames, Is.All.EqualTo("CloneStage"));
            Assert.That(
                tracer.RelatedEventMetadata.Select(metadata => $"{metadata["Stage"]}:{metadata["Attempt"]}:{metadata["State"]}"),
                Is.EqualTo(new[]
                {
                    "PreparingCommitAndTrees:1:Started",
                    "PreparingCommitAndTrees:1:Succeeded",
                    "InitializingIndex:1:Started",
                    "InitializingIndex:1:Retrying",
                    "PreparingCommitAndTrees:2:Started",
                    "PreparingCommitAndTrees:2:Succeeded",
                    "InitializingIndex:2:Started",
                    "InitializingIndex:2:Succeeded",
                    "FinalizingRepository:1:Started",
                    "FinalizingRepository:1:Succeeded",
                }));
            Assert.That(tracer.RelatedEventLevels, Is.All.EqualTo(EventLevel.Informational));
            Assert.That(tracer.RelatedEventMetadata.Select(metadata => (long)metadata["DurationMs"]), Is.All.GreaterThanOrEqualTo(0));
        }

        [TestCase("PreparingCommitAndTrees", 2)]
        [TestCase("InitializingIndex", 2)]
        [TestCase("FinalizingRepository", 1)]
        public void EarlyReturnRecordsTheUnfinishedStage(string stage, int attempt)
        {
            using MockTracer tracer = new MockTracer();
            using (CloneProgress progress = new CloneProgress(tracer))
            {
                progress.StartStage(CloneProgress.Stage.PreparingRepositoryAndCache);
                progress.StartStage(Enum.Parse<CloneProgress.Stage>(stage), attempt);
            }

            EventMetadata failure = tracer.RelatedEventMetadata.Last();
            Assert.That(failure["Stage"], Is.EqualTo(stage));
            Assert.That(failure["Attempt"], Is.EqualTo(attempt));
            Assert.That(failure["State"], Is.EqualTo("Failed"));
            Assert.That(tracer.RelatedEventMetadata.Count(metadata => (string)metadata["State"] == "Failed"), Is.EqualTo(1));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ExceptionRecordsTheCurrentStageWithoutReplacingTheException()
        {
            using MockTracer tracer = new MockTracer();
            using StringWriter output = new StringWriter();
            CloneVerb verb = new CloneVerb { Output = output };
            InvalidOperationException expected = new InvalidOperationException("clone failure");
            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                verb.RunCloneWithProgress(
                    tracer,
                    @"mock:\repo",
                    progress =>
                    {
                        progress.StartStage(CloneProgress.Stage.PreparingCheckout);
                        throw expected;
                    }));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(output.ToString(), Is.EqualTo(@"Cloning...Failed. Run 'gvfs log mock:\repo' for more info." + Environment.NewLine));
            EventMetadata failure = tracer.RelatedEventMetadata.Last();
            Assert.That(failure["Stage"], Is.EqualTo("PreparingCheckout"));
            Assert.That(failure["State"], Is.EqualTo("Failed"));
        }

        [TestCase("ReadingRemoteReferences", "Reading remote references")]
        [TestCase("PreparingRepositoryAndCache", "Preparing repository and cache")]
        [TestCase("PreparingCommitAndTrees", "Preparing commit and trees")]
        [TestCase("PreparingCheckout", "Preparing checkout")]
        [TestCase("InitializingIndex", "Initializing index")]
        [TestCase("FinalizingRepository", "Finalizing repository")]
        public void StatusRunnerReadsTheCurrentStageAndFitsTheConsole(string stage, string expectedMessage)
        {
            using MockTracer tracer = new MockTracer();
            TestableCloneVerb verb = new TestableCloneVerb();
            bool result = verb.RunCloneWithProgress(
                tracer,
                @"mock:\repo",
                progress =>
                {
                    progress.StartStage(Enum.Parse<CloneProgress.Stage>(stage));
                    Assert.That(verb.GetMessage(80), Is.EqualTo(expectedMessage));
                    foreach (int width in new[] { 20, 40 })
                    {
                        string message = verb.GetMessage(width);
                        Assert.That(("Cloning (" + message + ")...|").Length, Is.LessThan(width));
                        Assert.That(message, Does.EndWith("...").Or.EqualTo(expectedMessage));
                    }

                    Assert.That(verb.GetMessage(10), Is.Null);
                    return true;
                });

            Assert.That(result, Is.True);
            Assert.That(verb.GetMessage(80), Is.Null);
            Assert.That(tracer.RelatedEventMetadata[0]["Stage"], Is.EqualTo(stage));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void RetryMessageFitsFortyColumnsWithoutTruncatingTheLog(int attempt)
        {
            using MockTracer tracer = new MockTracer();
            TestableCloneVerb verb = new TestableCloneVerb();
            verb.RunCloneWithProgress(tracer, @"mock:\repo", progress =>
            {
                progress.StartStage(CloneProgress.Stage.PreparingCommitAndTrees, attempt);
                string message = verb.GetMessage(40);
                Assert.That(("Cloning (" + message + ")...|").Length, Is.LessThan(40));
                Assert.That(verb.GetMessage(80), Is.EqualTo("Preparing commit and trees" + (attempt == 2 ? " (retry)" : string.Empty)));
                return true;
            });

            Assert.That(tracer.RelatedEventMetadata[0]["Stage"], Is.EqualTo("PreparingCommitAndTrees"));
            Assert.That(tracer.RelatedEventMetadata[0]["Attempt"], Is.EqualTo(attempt));
        }

        private sealed class TestableCloneVerb : CloneVerb
        {
            private Func<string> getMessage;
            private int consoleWidth;

            public string GetMessage(int width)
            {
                this.consoleWidth = width;
                return this.getMessage();
            }

            protected override int GetCloneConsoleWidth()
            {
                return this.consoleWidth;
            }

            protected override bool ShowStatusWhileRunning(Func<bool> action, Func<string> getMessage, string message, string enlistmentRoot)
            {
                Assert.That(message, Is.EqualTo("Cloning"));
                this.getMessage = getMessage;
                return action();
            }
        }
    }
}
