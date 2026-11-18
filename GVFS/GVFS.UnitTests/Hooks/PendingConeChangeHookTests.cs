using GVFS.Common.NamedPipes;
using GVFS.Hooks;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class PendingConeChangeHookTests
    {
        private const string GitPid = "--git-pid=1234";

        // Tests that expect a reply use a long budget, so that a slow test machine cannot turn
        // the reply into a timeout.
        private const int ReplyBudgetMs = 30000;

        private bool pending;
        private bool pendingFileDeleted;
        private Queue<MockNamedPipeClient> clients;
        private List<MockNamedPipeClient> createdClients;
        private int lockRequestCount;
        private int lockProcessId;

        [SetUp]
        public void SetUp()
        {
            this.pending = true;
            this.pendingFileDeleted = false;
            this.clients = new Queue<MockNamedPipeClient>();
            this.createdClients = new List<MockNamedPipeClient>();
            this.lockRequestCount = 0;
            this.lockProcessId = 0;
        }

        [TestCase]
        public void NothingPendingSendsNothing()
        {
            this.pending = false;

            this.Apply(holdsLock: true, "status").ShouldEqual(ConeRequestOutcome.NothingPending);
            this.Apply(holdsLock: false, "diff").ShouldEqual(ConeRequestOutcome.NothingPending);

            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void CommandThatHoldsTheLockSendsAnApplyWithItsCommandId()
        {
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ConeManagement.SuccessResult));

            this.Apply(holdsLock: true, "commit", "-a", "-m", "message").ShouldEqual(ConeRequestOutcome.Applied);

            this.RequestHeaders().ShouldMatchInOrder(NamedPipeMessages.ConeManagement.ApplyRequest);
            this.createdClients[0].Requests[0].Body.ShouldEqual("1234");
            this.lockRequestCount.ShouldEqual(0);
        }

        [TestCase("log")]
        [TestCase("fetch")]
        [TestCase("git-diff")]
        public void UnlockedCommandThatDoesNotReadTheIndexSendsNothing(string command)
        {
            this.Apply(holdsLock: false, command).ShouldEqual(ConeRequestOutcome.NotAnIndexReader);

            this.createdClients.ShouldBeEmpty();
        }

        [TestCase("diff")]
        [TestCase("ls-files")]
        [TestCase("rev-parse")]
        [TestCase("show")]
        [TestCase("status")]
        public void UnlockedIndexReaderTakesTheLockForTheApplyAndReleasesIt(string command)
        {
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.AcquireLock.AcceptResult));
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ConeManagement.SuccessResult));
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ReleaseLock.SuccessResult));

            this.Apply(holdsLock: false, command).ShouldEqual(ConeRequestOutcome.Applied);

            this.RequestHeaders().ShouldMatchInOrder(
                NamedPipeMessages.AcquireLock.AcquireRequest,
                NamedPipeMessages.ConeManagement.ApplyRequest,
                NamedPipeMessages.ReleaseLock.Request);
            this.lockProcessId.ShouldEqual(1234, "The lock and the apply must name the same process");
        }

        [TestCase]
        public void UnlockedIndexReaderWaitsWhileTheMountProcessesChanges()
        {
            Queue<string> results = new Queue<string>(new[] { NamedPipeMessages.AcquireLock.DenyGVFSResult, NamedPipeMessages.AcquireLock.AcceptResult });
            this.clients.Enqueue(new MockNamedPipeClient { ReadResponseCallback = () => new NamedPipeMessages.Message(results.Dequeue(), null) });
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ConeManagement.SuccessResult));
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ReleaseLock.SuccessResult));

            this.Apply(holdsLock: false, "diff").ShouldEqual(ConeRequestOutcome.Applied);

            this.createdClients[0].Requests.Count.ShouldEqual(2);
        }

        [TestCase]
        public void UnlockedIndexReaderDoesNotWaitForAnotherGitCommand()
        {
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.AcquireLock.DenyGitResult));

            this.Apply(holdsLock: false, "diff").ShouldEqual(ConeRequestOutcome.LockUnavailable);

            this.RequestHeaders().ShouldMatchInOrder(NamedPipeMessages.AcquireLock.AcquireRequest);
        }

        [TestCase]
        public void UnlockedIndexReaderKeepsTheLockUntilTheMountFinishesAfterATimeout()
        {
            List<string> events = new List<string>();
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.AcquireLock.AcceptResult));
            this.clients.Enqueue(new MockNamedPipeClient
            {
                ReadResponseCallback = () =>
                {
                    // The mount replies after the hook stopped its wait.
                    Thread.Sleep(300);
                    lock (events)
                    {
                        events.Add("apply reply");
                    }

                    return new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.SuccessResult, null);
                },
            });
            this.clients.Enqueue(new MockNamedPipeClient
            {
                ReadResponseCallback = () =>
                {
                    lock (events)
                    {
                        events.Add("release");
                    }

                    return new NamedPipeMessages.Message(NamedPipeMessages.ReleaseLock.SuccessResult, null);
                },
            });

            this.CreateHook().TryApply(BuildArgs("diff"), holdsLock: false, this.CreateLockRequest, waitBudgetMs: 10)
                .ShouldEqual(ConeRequestOutcome.ReplyTimedOut);

            events.ShouldMatchInOrder("apply reply", "release");
        }

        [TestCase(NamedPipeMessages.ConeManagement.NotEnabledResult, "FeatureDisabled")]
        [TestCase(NamedPipeMessages.UnknownRequest, "Rejected")]
        public void MountThatNeverAppliesTheChangeDeletesThePendingFile(string reply, string expected)
        {
            this.clients.Enqueue(ReplyWith(reply));

            this.Apply(holdsLock: true, "status").ToString().ShouldEqual(expected);

            this.pendingFileDeleted.ShouldBeTrue();
        }

        [TestCase]
        public void MountThatIsNotReadyKeepsThePendingFile()
        {
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.MountNotReadyResult));

            this.Apply(holdsLock: true, "status").ShouldEqual(ConeRequestOutcome.Rejected);

            this.pendingFileDeleted.ShouldBeFalse();
        }

        [TestCase]
        public void ApplyForACommandThatDoesNotHoldTheLockKeepsThePendingFile()
        {
            this.clients.Enqueue(ReplyWith(NamedPipeMessages.ConeManagement.NotLockHolderResult));

            this.Apply(holdsLock: true, "add").ShouldEqual(ConeRequestOutcome.NotLockHolder);

            this.pendingFileDeleted.ShouldBeFalse();
        }

        [TestCase]
        public void UnlockedIndexReaderWithoutAProcessIdSendsNothing()
        {
            this.CreateHook().TryApply(new[] { "pre-command", "diff" }, holdsLock: false, this.CreateLockRequest, ReplyBudgetMs)
                .ShouldEqual(ConeRequestOutcome.Failed);

            this.createdClients.ShouldBeEmpty();
        }

        private static string[] BuildArgs(params string[] command)
        {
            List<string> args = new List<string> { "pre-command" };
            args.AddRange(command);
            args.Add(GitPid);
            return args.ToArray();
        }

        private static MockNamedPipeClient ReplyWith(string responseHeader)
        {
            return new MockNamedPipeClient { Response = new NamedPipeMessages.Message(responseHeader, null) };
        }

        private ConeRequestOutcome Apply(bool holdsLock, params string[] command)
        {
            return this.CreateHook().TryApply(BuildArgs(command), holdsLock, this.CreateLockRequest, ReplyBudgetMs);
        }

        private NamedPipeMessages.LockRequest CreateLockRequest(int processId)
        {
            this.lockRequestCount++;
            this.lockProcessId = processId;
            return new NamedPipeMessages.LockRequest(processId, isElevated: false, checkAvailabilityOnly: false, "git diff", gitCommandSessionId: string.Empty);
        }

        private List<string> RequestHeaders()
        {
            lock (this.createdClients)
            {
                return this.createdClients.SelectMany(client => client.Requests).Select(request => request.Header).ToList();
            }
        }

        private PendingConeChangeHook CreateHook()
        {
            return new PendingConeChangeHook(
                () => this.pending,
                () => this.pendingFileDeleted = true,
                () =>
                {
                    lock (this.createdClients)
                    {
                        MockNamedPipeClient client = this.clients.Dequeue();
                        this.createdClients.Add(client);
                        return client;
                    }
                },
                trace: null);
        }
    }
}