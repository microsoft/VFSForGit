using GVFS.Common.NamedPipes;
using GVFS.Hooks;
using GVFS.UnitTests.Mock.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class ConeManagementHookTests
    {
        private const string Root = "C:\\repo\\src";
        private const string Header = "/*\n!/*/\n";
        private const string ConeWithRecursiveA = Header + "/A/\n";
        private const string ConeWithParentOnlyA = Header + "/A/\n!/A/*/\n";
        private const string GitPid = "--git-pid=1234";

        // Tests that expect a reply use a long budget, so that a slow test machine cannot turn
        // the reply into a timeout.
        private const int ReplyBudgetMs = 30000;

        private bool featureEnabled;
        private int configReadCount;
        private string coneFileContent;
        private List<MockNamedPipeClient> createdClients;
        private Func<MockNamedPipeClient> clientFactory;

        [SetUp]
        public void SetUp()
        {
            this.featureEnabled = true;
            this.configReadCount = 0;
            this.coneFileContent = ConeWithRecursiveA;
            this.createdClients = new List<MockNamedPipeClient>();
            this.clientFactory = () => ReplyWith(NamedPipeMessages.ConeManagement.SuccessResult);
        }

        [TestCase]
        public void WidenWithFeatureOffSendsNothing()
        {
            this.featureEnabled = false;

            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.FeatureDisabled);

            this.configReadCount.ShouldEqual(1);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase(null)]
        [TestCase("/.gitattributes\r\n")]
        [TestCase("/A/\n")]
        public void WithoutConeModeFileNeitherHookReadsConfigOrSends(string content)
        {
            this.coneFileContent = content;

            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.NoConeFile);
            this.Narrow("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.NoConeFile);

            this.configReadCount.ShouldEqual(0);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase("status")]
        [TestCase("commit", "-m", "message")]
        [TestCase("checkout", "topic")]
        public void WidenForCommandWithoutPathsDoesNotReadConfig(params string[] command)
        {
            this.Widen(command).ShouldEqual(ConeRequestOutcome.NoPathsNamed);

            this.configReadCount.ShouldEqual(0);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenTreatsAnAmbiguousRefOrPathAsARef()
        {
            // Without "--", git diff can read "Z/z.txt" as a revision, so the parser does not
            // count it as a path, and the hook does not widen.
            this.Widen("diff", "HEAD", "Z/z.txt").ShouldEqual(ConeRequestOutcome.NoPathsNamed);
        }

        [TestCase("add", "A/a.txt")]
        [TestCase("add", "A/deep/er/")]
        [TestCase("rm", "root.txt")]
        [TestCase("restore", "--staged", "A/a.txt", "root.txt")]
        [TestCase("checkout", "HEAD", "--", "A/a.txt")]
        public void WidenForInConePathsSendsNoRequest(params string[] command)
        {
            this.Widen(command).ShouldEqual(ConeRequestOutcome.SkippedInCone);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenUsesTheRepositoryCaseRule()
        {
            this.CreateHook(ignoreCase: true).TrySendWiden(BuildArgs("add", "a/a.txt"), ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.SkippedInCone);
            this.CreateHook(ignoreCase: false).TrySendWiden(BuildArgs("add", "a/a.txt"), ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.Applied);
        }

        [TestCase]
        public void WidenSendsOnlyTheOutOfConePaths()
        {
            this.Widen("add", "A/a.txt", "Z/z.txt", ":(glob)**/*.cs", ".").ShouldEqual(ConeRequestOutcome.Applied);

            NamedPipeMessages.ConeManagement.WidenParameters parameters = this.GetSingleWidenRequest();
            parameters.CommandId.ShouldEqual("1234");
            parameters.Paths.ShouldMatchInOrder("Z/z.txt");
        }

        [TestCase("A/")]
        [TestCase("Z/")]
        public void WidenForFolderNeedsTheWholeSubtreeInCone(string folder)
        {
            this.coneFileContent = ConeWithParentOnlyA;

            this.Widen("add", folder).ShouldEqual(ConeRequestOutcome.Applied);
            this.GetSingleWidenRequest().Paths.ShouldMatchInOrder(folder);
        }

        [TestCase]
        public void WidenResolvesRelativePathsAgainstGitPrefix()
        {
            this.coneFileContent = ConeWithParentOnlyA;

            this.CreateHook(gitPrefix: "B/").TrySendWiden(BuildArgs("add", "x.txt"), ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.Applied);
            this.GetSingleWidenRequest().Paths.ShouldMatchInOrder("B/x.txt");

            this.createdClients.Clear();
            this.CreateHook(gitPrefix: "A/").TrySendWiden(BuildArgs("add", "x.txt"), ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.SkippedInCone);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase(Header + "/A*/\n", false)]
        [TestCase(Header + "/A/\n/a/\n", true)]
        public void WidenSendsEveryPathWhenTheConeFileCannotBeTrusted(string content, bool ignoreCase)
        {
            this.coneFileContent = content;

            this.CreateHook(ignoreCase: ignoreCase).TrySendWiden(BuildArgs("add", "A/a.txt"), ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.Applied);
            this.GetSingleWidenRequest().Paths.ShouldMatchInOrder("A/a.txt");
        }

        [TestCase("add", "C:\\other\\file.txt")]
        [TestCase("add", "../outside.txt")]
        [TestCase("add", "A/*/x.txt")]
        [TestCase("add", "Z/frame\u0003end.txt")]
        [TestCase("add", ":(glob)**/*.cs")]
        [TestCase("add", ".")]
        [TestCase("add", "--pathspec-from-file=list.txt")]
        public void WidenSendsNothingForPathsWithoutAConeEntry(params string[] command)
        {
            this.Widen(command).ShouldEqual(ConeRequestOutcome.NoResolvablePaths);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase("-C", "sub")]
        [TestCase("--git-dir=other")]
        [TestCase("--work-tree", "other")]
        public void WidenSendsNothingWhenAGlobalOptionChangesTheRepository(params string[] globalOptions)
        {
            List<string> command = new List<string>(globalOptions) { "add", "Z/z.txt" };

            this.Widen(command.ToArray()).ShouldEqual(ConeRequestOutcome.NoResolvablePaths);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenForStashSendsNoRequest()
        {
            this.Widen("stash", "push", "--", "Z/z.txt").ShouldEqual(ConeRequestOutcome.SkippedStash);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase(NamedPipeMessages.UnknownRequest)]
        [TestCase(NamedPipeMessages.MountNotReadyResult)]
        [TestCase(NamedPipeMessages.ConeManagement.FailureResult)]
        [TestCase(NamedPipeMessages.ConeManagement.NotEnabledResult)]
        public void WidenWithNonSuccessReplyLetsGitRun(string responseHeader)
        {
            this.clientFactory = () => ReplyWith(responseHeader);

            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.Rejected);
            this.createdClients[0].Requests.Count.ShouldEqual(1);
        }

        [TestCase]
        public void WidenWithoutMountLetsGitRun()
        {
            this.clientFactory = NoMount;

            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.MountUnavailable);
            this.createdClients[0].Requests.ShouldBeEmpty();
            this.createdClients[0].ConnectTimeoutMs.ShouldEqual(NamedPipeMessages.ConeManagement.ConnectTimeoutMs);
            this.createdClients[0].Disposed.ShouldBeTrue();
        }

        [TestCase]
        public void WidenThatTimesOutLetsGitRun()
        {
            // The event is not disposed, because the abandoned request task still waits on it.
            ManualResetEventSlim releaseReply = new ManualResetEventSlim(initialState: false);
            this.clientFactory = () => ReplyAfter(releaseReply);

            Stopwatch stopwatch = Stopwatch.StartNew();
            this.CreateHook().TrySendWiden(BuildArgs("add", "Z/z.txt"), waitBudgetMs: 50).ShouldEqual(ConeRequestOutcome.TimedOut);
            stopwatch.Stop();
            releaseReply.Set();

            (stopwatch.ElapsedMilliseconds < 5000).ShouldBeTrue("The hook must not wait for the reply after the budget ends.");
        }

        [TestCase]
        public void WidenThatThrowsLetsGitRun()
        {
            this.clientFactory = () => new MockNamedPipeClient { SendError = new BrokenPipeException("send failed", null) };
            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.Failed);

            this.clientFactory = () => throw new InvalidOperationException("no client");
            this.Widen("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.Failed);

            ConeManagementHook throwingConfig = new ConeManagementHook(
                (out bool ignoreCase) => throw new InvalidOperationException("config"),
                () => this.coneFileContent,
                () => throw new InvalidOperationException("no client"),
                Root,
                () => Root,
                null,
                null);
            throwingConfig.TrySendWiden(BuildArgs("add", "Z/z.txt")).ShouldEqual(ConeRequestOutcome.Failed);
            throwingConfig.TrySendNarrow(BuildArgs("add", "Z/z.txt")).ShouldEqual(ConeRequestOutcome.Failed);
        }

        [TestCase]
        public void NarrowWithFeatureOffSendsNothing()
        {
            this.featureEnabled = false;

            this.Narrow("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.FeatureDisabled);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void NarrowSendsTheGitProcessIdFromThePostCommandArguments()
        {
            string[] postCommandArgs = new[] { "post-command", "add", "A/a.txt", "--git-pid=1234", "--exit_code=0" };

            this.CreateHook().TrySendNarrow(postCommandArgs, ReplyBudgetMs).ShouldEqual(ConeRequestOutcome.Applied);

            NamedPipeMessages.Message request = this.createdClients[0].Requests[0];
            request.Header.ShouldEqual(NamedPipeMessages.ConeManagement.NarrowRequest);
            NamedPipeMessages.ConeManagement.NarrowParameters.TryParse(request.Body, out NamedPipeMessages.ConeManagement.NarrowParameters parameters).ShouldBeTrue();
            parameters.CommandId.ShouldEqual("1234");
        }

        [TestCase]
        public void NarrowSkipsCommandsThatCannotWiden()
        {
            this.Narrow("status").ShouldEqual(ConeRequestOutcome.NoPathsNamed);
            this.Narrow("stash", "push", "--", "Z/z.txt").ShouldEqual(ConeRequestOutcome.SkippedStash);
            this.createdClients.ShouldBeEmpty();
        }

        [TestCase]
        public void NarrowFailuresLetGitFinish()
        {
            this.clientFactory = NoMount;
            this.Narrow("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.MountUnavailable);

            this.clientFactory = () => ReplyWith(NamedPipeMessages.UnknownRequest);
            this.Narrow("add", "Z/z.txt").ShouldEqual(ConeRequestOutcome.Rejected);

            ManualResetEventSlim releaseReply = new ManualResetEventSlim(initialState: false);
            this.clientFactory = () => ReplyAfter(releaseReply);
            this.CreateHook().TrySendNarrow(BuildArgs("add", "Z/z.txt"), waitBudgetMs: 50).ShouldEqual(ConeRequestOutcome.TimedOut);
            releaseReply.Set();
        }

        [TestCase]
        public void FailedRequestsWriteADiagnostic()
        {
            List<string> messages = new List<string>();
            this.clientFactory = () => ReplyWith(NamedPipeMessages.UnknownRequest);

            this.CreateHook(trace: messages.Add).TrySendWiden(BuildArgs("add", "Z/z.txt"), ReplyBudgetMs);
            this.CreateHook(trace: messages.Add).TrySendNarrow(BuildArgs("add", "Z/z.txt"), ReplyBudgetMs);

            messages.ShouldMatchInOrder("widen not applied: Rejected", "narrow not applied: Rejected");
        }

        [TestCase(new[] { "pre-command", "add", "a.txt", "--git-pid=77" }, "77")]
        [TestCase(new[] { "post-command", "add", "a.txt", "--git-pid=77", "--exit_code=0" }, "77")]
        [TestCase(new[] { "pre-command", "diff", "--", "--git-pid=99", "--git-pid=77" }, "77")]
        [TestCase(new[] { "pre-command", "add", "a.txt", "--git-pid=12a" }, "")]
        [TestCase(new[] { "pre-command", "add", "a.txt" }, "")]
        public void CommandIdIsTheGitProcessId(string[] args, string expected)
        {
            ConeManagementHook.GetCommandId(args).ShouldEqual(expected);
        }

        private static string[] BuildArgs(params string[] command)
        {
            List<string> args = new List<string> { "pre-command" };
            args.AddRange(command);
            args.Add(GitPid);
            return args.ToArray();
        }

        private ConeRequestOutcome Widen(params string[] command)
        {
            return this.CreateHook().TrySendWiden(BuildArgs(command), ReplyBudgetMs);
        }

        private ConeRequestOutcome Narrow(params string[] command)
        {
            return this.CreateHook().TrySendNarrow(BuildArgs(command), ReplyBudgetMs);
        }

        private ConeManagementHook CreateHook(string gitPrefix = null, bool ignoreCase = false, Action<string> trace = null)
        {
            return new ConeManagementHook(
                (out bool readIgnoreCase) =>
                {
                    this.configReadCount++;
                    readIgnoreCase = ignoreCase;
                    return this.featureEnabled;
                },
                () => this.coneFileContent,
                () =>
                {
                    MockNamedPipeClient client = this.clientFactory();
                    this.createdClients.Add(client);
                    return client;
                },
                Root,
                () => Root,
                gitPrefix,
                trace);
        }

        private NamedPipeMessages.ConeManagement.WidenParameters GetSingleWidenRequest()
        {
            this.createdClients.Count.ShouldEqual(1);
            this.createdClients[0].Requests.Count.ShouldEqual(1);

            NamedPipeMessages.Message request = this.createdClients[0].Requests[0];
            request.Header.ShouldEqual(NamedPipeMessages.ConeManagement.WidenRequest);
            NamedPipeMessages.ConeManagement.WidenParameters.TryParse(request.Body, out NamedPipeMessages.ConeManagement.WidenParameters parameters).ShouldBeTrue();
            return parameters;
        }

        private static MockNamedPipeClient ReplyWith(string responseHeader)
        {
            return new MockNamedPipeClient { Response = new NamedPipeMessages.Message(responseHeader, null) };
        }

        private static MockNamedPipeClient ReplyAfter(ManualResetEventSlim releaseReply)
        {
            return new MockNamedPipeClient
            {
                ReadResponseCallback = () =>
                {
                    releaseReply.Wait();
                    return new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.SuccessResult, null);
                },
            };
        }

        private static MockNamedPipeClient NoMount()
        {
            return new MockNamedPipeClient { ConnectError = new IOException("no mount") };
        }    }
}
