using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Prefetch;
using GVFS.Mount;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Mount
{
    [TestFixture]
    [NonParallelizable]
    public class PrefetchUnmountTests
    {
        [TestCase("legacy", false)]
        [TestCase("progress", false)]
        [TestCase("progress", true)]
        [TestCase("blobs", false)]
        [Timeout(15000)]
        public async Task UnmountWaitsForWorkAndCompletionBeforeDisposal(string kind, bool disconnect)
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer);
            using ManualResetEventSlim workEntered = new ManualResetEventSlim();
            using ManualResetEventSlim finishWork = new ManualResetEventSlim();
            using ManualResetEventSlim completionEntered = new ManualResetEventSlim();
            using ManualResetEventSlim finishCompletion = new ManualResetEventSlim();
            using ManualResetEventSlim waiting = new ManualResetEventSlim();
            using ManualResetEventSlim disconnected = new ManualResetEventSlim();
            mount.Work = () =>
            {
                workEntered.Set();
                Assert.That(finishWork.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(mount.Context.Disposed, Is.False);
            };
            RecordingConnection prefetch = new RecordingConnection(tracer, response =>
            {
                if (response.Header == NamedPipeMessages.PrefetchCommits.StartedResult)
                {
                    return true;
                }

                if (response.Header == NamedPipeMessages.PrefetchCommits.ProgressResult && disconnect)
                {
                    disconnected.Set();
                    return false;
                }

                if (response.Header == NamedPipeMessages.PrefetchCommits.CompleteResult
                    || response.Header == NamedPipeMessages.PrefetchBlobs.CompleteResult)
                {
                    completionEntered.Set();
                    Assert.That(finishCompletion.Wait(TimeSpan.FromSeconds(10)), Is.True);
                    Assert.That(mount.Context.Disposed, Is.False);
                }

                return !disconnect;
            });
            RecordingConnection unmount = new RecordingConnection(tracer, response =>
            {
                if (response.Header == NamedPipeMessages.Unmount.Waiting)
                {
                    waiting.Set();
                }

                return true;
            });
            Task request = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch(kind).ToString(), prefetch));
            Task teardown = Task.CompletedTask;
            try
            {
                Assert.That(workEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                if (disconnect)
                {
                    Assert.That(disconnected.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }

                teardown = Task.Run(() => mount.HandleRequest(tracer, "Unmount|ReportProgress", unmount));
                Assert.That(waiting.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(mount.Context.Disposed, Is.False);
                Assert.That(mount.Stops, Is.Zero);
                foreach (string rejectedKind in new[] { "legacy", "blobs" })
                {
                    RecordingConnection rejected = new RecordingConnection(tracer);
                    mount.HandleRequest(tracer, CreatePrefetch(rejectedKind).ToString(), rejected);
                    Assert.That(rejected.Responses.Single().Header, Is.EqualTo("MountNotReady"));
                }

                RecordingConnection secondUnmount = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, "Unmount", secondUnmount);
                Assert.That(secondUnmount.Responses.Single().Header, Is.EqualTo(NamedPipeMessages.Unmount.AlreadyUnmounting));
                Assert.That(unmount.Responses.Last().Body, Does.Contain("requester PID 1234").And.Contain($"mount PID {Environment.ProcessId}"));

                finishWork.Set();
                Assert.That(completionEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(mount.Context.Disposed, Is.False);
                Assert.That(mount.Stops, Is.Zero);
                finishCompletion.Set();
                await Task.WhenAll(request, teardown).WaitAsync(TimeSpan.FromSeconds(5));

                Assert.That(mount.Context.Disposed, Is.True);
                Assert.That(mount.Stops, Is.EqualTo(1));
                Assert.That(mount.ExitCode, Is.EqualTo(ReturnCode.Success));
                Assert.That(unmount.Responses.Last().Header, Is.EqualTo(NamedPipeMessages.Unmount.Completed));
            }
            finally
            {
                finishWork.Set();
                finishCompletion.Set();
                await Task.WhenAll(request, teardown).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [TestCase]
        [Timeout(15000)]
        public async Task UnmountWaitsForEveryAcceptedRequest()
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer);
            using CountdownEvent entered = new CountdownEvent(2);
            using ManualResetEventSlim finishCommits = new ManualResetEventSlim();
            using ManualResetEventSlim finishBlobs = new ManualResetEventSlim();
            using ManualResetEventSlim waiting = new ManualResetEventSlim();
            int calls = 0;
            mount.Work = () =>
            {
                int call = Interlocked.Increment(ref calls);
                entered.Signal();
                Assert.That((call == 1 ? finishCommits : finishBlobs).Wait(TimeSpan.FromSeconds(10)), Is.True);
            };
            Task commits = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch("legacy").ToString(), new RecordingConnection(tracer)));
            Task blobs = Task.CompletedTask;
            Task teardown = Task.CompletedTask;
            try
            {
                Assert.That(SpinWait.SpinUntil(() => entered.CurrentCount == 1, TimeSpan.FromSeconds(5)), Is.True);
                blobs = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch("blobs").ToString(), new RecordingConnection(tracer)));
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                RecordingConnection unmount = new RecordingConnection(tracer, response =>
                {
                    if (response.Header == NamedPipeMessages.Unmount.Waiting)
                    {
                        waiting.Set();
                    }

                    return true;
                });
                teardown = Task.Run(() => mount.HandleRequest(tracer, "Unmount|ReportProgress", unmount));
                Assert.That(waiting.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(unmount.Responses.Last().Body, Does.Contain("commit prefetch").And.Contain("blob prefetch"));
                finishCommits.Set();
                await commits.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(mount.Context.Disposed, Is.False);
                Assert.That(mount.Stops, Is.Zero);
                finishBlobs.Set();
                await Task.WhenAll(blobs, teardown).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(mount.Context.Disposed, Is.True);
            }
            finally
            {
                finishCommits.Set();
                finishBlobs.Set();
                await Task.WhenAll(commits, blobs, teardown).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [TestCase]
        public void DisconnectedProgressClientBeforeStartDoesNotLeakAdmission()
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer) { Work = () => Assert.Fail("Prefetch must not start") };
            mount.HandleRequest(tracer, CreatePrefetch("progress").ToString(), new RecordingConnection(tracer, _ => false));
            RecordingConnection unmount = new RecordingConnection(tracer);
            mount.HandleRequest(tracer, "Unmount", unmount);
            Assert.That(mount.Context.Disposed, Is.True);
            Assert.That(unmount.Responses.Last().Header, Is.EqualTo(NamedPipeMessages.Unmount.Completed));
        }

        [TestCase("legacy")]
        [TestCase("progress")]
        [TestCase("blobs")]
        [Timeout(15000)]
        public async Task TimeoutKeepsMountAliveAndAllowsRetry(string kind)
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer) { DrainTimeout = TimeSpan.Zero };
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim finish = new ManualResetEventSlim();
            mount.Work = () =>
            {
                entered.Set();
                Assert.That(finish.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(mount.Context.Disposed, Is.False);
            };
            Task request = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch(kind).ToString(), new RecordingConnection(tracer)));
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                RecordingConnection unmount = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, "Unmount", unmount);
                Assert.That(unmount.Responses.Select(response => response.Header),
                    Is.EqualTo(new[] { NamedPipeMessages.Unmount.Acknowledged, NamedPipeMessages.Unmount.TimedOut }));
                Assert.That(unmount.Responses.Last().Body, Does.Contain("stays mounted").And.Contain("requester PID 1234"));
                Assert.That(mount.Context.Disposed, Is.False);
                Assert.That(mount.Stops, Is.Zero);
                Assert.That(mount.ExitCode, Is.Null);

                mount.Work = () => Assert.That(mount.Context.Disposed, Is.False);
                RecordingConnection afterTimeout = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, CreatePrefetch("legacy").ToString(), afterTimeout);
                Assert.That(afterTimeout.Responses.Single().Header, Is.EqualTo(NamedPipeMessages.PrefetchCommits.CompleteResult));

                finish.Set();
                await request.WaitAsync(TimeSpan.FromSeconds(5));
                RecordingConnection retry = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, "Unmount", retry);
                Assert.That(retry.Responses.Last().Header, Is.EqualTo(NamedPipeMessages.Unmount.Completed));
                Assert.That(mount.Context.Disposed, Is.True);
            }
            finally
            {
                finish.Set();
                await request.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [TestCase("progress")]
        [TestCase("blobs")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailedWorkReleasesItsLifetime(string kind)
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer) { Work = () => throw new InvalidOperationException("Synthetic failure") };
            RecordingConnection prefetch = new RecordingConnection(tracer);
            mount.HandleRequest(tracer, CreatePrefetch(kind).ToString(), prefetch);
            Assert.That(prefetch.Responses.Last().Body, Does.Contain("Synthetic failure"));
            RecordingConnection unmount = new RecordingConnection(tracer);
            mount.HandleRequest(tracer, "Unmount", unmount);
            Assert.That(mount.Context.Disposed, Is.True);
            Assert.That(unmount.Responses.Last().Header, Is.EqualTo(NamedPipeMessages.Unmount.Completed));
        }

        [TestCase]
        [Timeout(15000)]
        public async Task DehydrateTimeoutDoesNotStopOrRemountCallbacks()
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer) { DrainTimeout = TimeSpan.Zero };
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim finish = new ManualResetEventSlim();
            mount.Work = () =>
            {
                entered.Set();
                Assert.That(finish.Wait(TimeSpan.FromSeconds(10)), Is.True);
            };
            Task prefetch = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch("blobs").ToString(), new RecordingConnection(tracer)));
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                RecordingConnection dehydrate = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, new NamedPipeMessages.DehydrateFolders.Request
                {
                    Folders = "folder",
                    BackupFolderPath = @"mock:\backup",
                }.CreateMessage().ToString(), dehydrate);
                NamedPipeMessages.DehydrateFolders.Response response = NamedPipeMessages.DehydrateFolders.Response.FromMessage(dehydrate.Responses.Single());
                Assert.That(response.FailedFolders.Single(), Does.Contain("stays mounted").And.Contain("requester PID 1234"));
                Assert.That(mount.Stops, Is.Zero);
                Assert.That(mount.Remounts, Is.Zero);
                Assert.That(mount.Context.Disposed, Is.False);
            }
            finally
            {
                finish.Set();
                await prefetch.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [TestCase]
        [Timeout(15000)]
        public async Task SameProcessRemountWaitsAndReopensAdmissionOnlyAfterCallbacksStart()
        {
            using MockTracer tracer = new MockTracer();
            TestMount mount = new TestMount(tracer);
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim finish = new ManualResetEventSlim();
            using ManualResetEventSlim remountEntered = new ManualResetEventSlim();
            using ManualResetEventSlim finishRemount = new ManualResetEventSlim();
            mount.Work = () =>
            {
                entered.Set();
                Assert.That(finish.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(mount.Stops, Is.Zero);
            };
            mount.Remount = () =>
            {
                remountEntered.Set();
                Assert.That(finishRemount.Wait(TimeSpan.FromSeconds(10)), Is.True);
            };
            Task prefetch = Task.Run(() => mount.HandleRequest(tracer, CreatePrefetch("legacy").ToString(), new RecordingConnection(tracer)));
            Task remount = Task.CompletedTask;
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                remount = Task.Run(() => mount.BackupFoldersWhileUnmounted(
                    new NamedPipeMessages.DehydrateFolders.Request(),
                    new NamedPipeMessages.DehydrateFolders.Response(),
                    Array.Empty<string>()));
                Assert.That(SpinWait.SpinUntil(() => mount.State == "Mounting", TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(mount.Stops, Is.Zero);
                finish.Set();
                Assert.That(remountEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                RecordingConnection rejected = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, CreatePrefetch("legacy").ToString(), rejected);
                Assert.That(rejected.Responses.Single().Header, Is.EqualTo(NamedPipeMessages.MountNotReadyResult));
                Assert.That(mount.Context.Disposed, Is.False);
                finishRemount.Set();
                await Task.WhenAll(prefetch, remount).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(mount.Stops, Is.EqualTo(1));
                Assert.That(mount.Remounts, Is.EqualTo(1));
                mount.Work = () => { };
                RecordingConnection accepted = new RecordingConnection(tracer);
                mount.HandleRequest(tracer, CreatePrefetch("blobs").ToString(), accepted);
                Assert.That(accepted.Responses.Single().Header, Is.EqualTo(NamedPipeMessages.PrefetchBlobs.CompleteResult));
            }
            finally
            {
                finish.Set();
                finishRemount.Set();
                await Task.WhenAll(prefetch, remount).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private static NamedPipeMessages.Message CreatePrefetch(string kind)
        {
            return kind == "blobs"
                ? new NamedPipeMessages.PrefetchBlobs.Request
                {
                    Files = new List<string> { "file" },
                    Folders = new List<string>(),
                    HeadCommitId = new string('a', 40),
                }.CreateMessage()
                : new NamedPipeMessages.Message(NamedPipeMessages.PrefetchCommits.Request,
                    kind == "progress" ? NamedPipeMessages.PrefetchCommits.ReportProgress : null);
        }

        private sealed class RecordingConnection : NamedPipeServer.Connection
        {
            public RecordingConnection(MockTracer tracer, Func<NamedPipeMessages.Message, bool> send = null)
                : base(null, tracer, () => false)
            {
                this.Send = send;
            }

            public ConcurrentQueue<NamedPipeMessages.Message> Responses { get; } = new ConcurrentQueue<NamedPipeMessages.Message>();
            public Func<NamedPipeMessages.Message, bool> Send { get; set; }

            public override int? GetClientProcessId() => 1234;

            public override bool TrySendResponse(string message)
            {
                NamedPipeMessages.Message response = NamedPipeMessages.Message.FromString(message);
                this.Responses.Enqueue(response);
                return this.Send?.Invoke(response) ?? true;
            }
        }

        private sealed class RecordingContext : GVFSContext
        {
            public RecordingContext(MockTracer tracer, GVFSEnlistment enlistment)
                : base(tracer, null, null, enlistment)
            {
            }

            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                this.Disposed = true;
            }
        }

        private sealed class TestMount : InProcessMount
        {
            public TestMount(MockTracer tracer)
                : base(tracer, new MockGVFSEnlistment(), "mount", null, null, null, null, false)
            {
                FieldInfo state = typeof(InProcessMount).GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance);
                state.SetValue(this, Enum.Parse(state.FieldType, "Ready"));
                this.Context = new RecordingContext(tracer, new MockGVFSEnlistment());
                typeof(InProcessMount).GetField("context", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, this.Context);
            }

            public RecordingContext Context { get; }
            public Action Work { get; set; } = () => { };
            public Action Remount { get; set; } = () => { };
            public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
            public int Stops { get; private set; }
            public int Remounts { get; private set; }
            public ReturnCode? ExitCode { get; private set; }
            public string State => typeof(InProcessMount).GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this).ToString();

            protected override TimeSpan PrefetchDrainTimeout => this.DrainTimeout;

            protected override NamedPipeMessages.PrefetchCommits.Response PrefetchCommits(PrefetchProgress progress)
            {
                this.Work();
                return new NamedPipeMessages.PrefetchCommits.Response { Success = true };
            }

            protected override NamedPipeMessages.PrefetchBlobs.Response PrefetchBlobs(NamedPipeMessages.PrefetchBlobs.Request request)
            {
                this.Work();
                return new NamedPipeMessages.PrefetchBlobs.Response { Success = true };
            }

            protected override void StopWorkingDirectoryCallbacks(bool willRemountInSameProcess)
            {
                this.Stops++;
                base.StopWorkingDirectoryCallbacks(willRemountInSameProcess);
            }

            protected override void MountAndStartWorkingDirectoryCallbacks(CacheServerInfo cache, bool alreadyInitialized = false)
            {
                this.Remounts++;
                this.Remount();
            }

            protected override void ExitMount(ReturnCode returnCode)
            {
                this.ExitCode = returnCode;
            }
        }
    }
}
