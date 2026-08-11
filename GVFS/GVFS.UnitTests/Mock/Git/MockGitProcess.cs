using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGitProcess : GitProcess
    {
        private List<CommandInfo> expectedCommandInfos = new List<CommandInfo>();

        public MockGitProcess()
            : base(new MockGVFSEnlistment())
        {
            this.CommandsRun = new List<string>();
            this.InvokedTimeoutMs = new List<int>();
            this.LastInvokedTimeoutMs = null;
            this.LastInvokedCancellationToken = CancellationToken.None;
            this.StoredCredentials = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
            this.CredentialApprovals = new Dictionary<string, List<Credential>>();
            this.CredentialRejections = new Dictionary<string, List<Credential>>();
        }

        public List<string> CommandsRun { get; }

        /// <summary>
        /// The timeout passed to every InvokeGitImpl call, in order. Lets tests assert that a
        /// caller actually plumbed a finite timeout rather than defaulting to -1 (infinite).
        /// </summary>
        public List<int> InvokedTimeoutMs { get; }

        /// <summary>
        /// The timeout passed to the most recent InvokeGitImpl call, or null if none has run.
        /// </summary>
        public int? LastInvokedTimeoutMs { get; private set; }

        /// <summary>
        /// The cancellation token passed to the most recent InvokeGitImpl call. Lets tests assert
        /// that a caller plumbed a real (cancelable) token down to the git invocation.
        /// </summary>
        public CancellationToken LastInvokedCancellationToken { get; private set; }

        /// <summary>
        /// When set, InvokeGitImpl blocks until this event is signaled or the caller's token is
        /// canceled. Lets tests simulate a slow/hung git credential process and prove that
        /// cancellation interrupts it and that shared resources are not held meanwhile.
        /// </summary>
        public ManualResetEventSlim BlockInvokeUntilSignaled { get; set; }

        /// <summary>
        /// Signaled by InvokeGitImpl right before it starts blocking on
        /// <see cref="BlockInvokeUntilSignaled"/>. Lets a test wait until the git invocation is
        /// actually in-flight before it inspects shared state or cancels.
        /// </summary>
        public ManualResetEventSlim InvokeReachedBlock { get; set; }

        public bool ShouldFail { get; set; }
        public Dictionary<string, Credential> StoredCredentials { get; }
        public Dictionary<string, List<Credential>> CredentialApprovals { get; }
        public Dictionary<string, List<Credential>> CredentialRejections { get; }

        public void SetExpectedCommandResult(string command, Func<Result> result, bool matchPrefix = false)
        {
            CommandInfo commandInfo = new CommandInfo(command, result, matchPrefix);
            this.expectedCommandInfos.Add(commandInfo);
        }

        public override bool TryStoreCredential(ITracer tracer, string repoUrl, string username, string password, out string error, int timeoutMs = -1, CancellationToken cancellationToken = default)
        {
            Credential credential = new Credential(username, password);

            // Record the approval request for this credential
            List<Credential> acceptedCredentials;
            if (!this.CredentialApprovals.TryGetValue(repoUrl, out acceptedCredentials))
            {
                acceptedCredentials = new List<Credential>();
                this.CredentialApprovals[repoUrl] = acceptedCredentials;
            }

            acceptedCredentials.Add(credential);

            // Store the credential
            this.StoredCredentials[repoUrl] = credential;

            return base.TryStoreCredential(tracer, repoUrl, username, password, out error, timeoutMs, cancellationToken);
        }

        public override bool TryDeleteCredential(ITracer tracer, string repoUrl, string username, string password, out string error, int timeoutMs = -1, CancellationToken cancellationToken = default)
        {
            Credential credential = new Credential(username, password);

            // Record the rejection request for this credential
            List<Credential> rejectedCredentials;
            if (!this.CredentialRejections.TryGetValue(repoUrl, out rejectedCredentials))
            {
                rejectedCredentials = new List<Credential>();
                this.CredentialRejections[repoUrl] = rejectedCredentials;
            }

            rejectedCredentials.Add(credential);

            // Erase the credential
            this.StoredCredentials.Remove(repoUrl);

            return base.TryDeleteCredential(tracer, repoUrl, username, password, out error, timeoutMs, cancellationToken);
        }

        protected override Result InvokeGitImpl(
            string command,
            string workingDirectory,
            string dotGitDirectory,
            bool useReadObjectHook,
            Action<StreamWriter> writeStdIn,
            Action<string> parseStdOutLine,
            int timeoutMs,
            string gitObjectsDirectory = null,
            bool usePrecommandHook = true,
            CancellationToken cancellationToken = default)
        {
            this.CommandsRun.Add(command);
            this.LastInvokedTimeoutMs = timeoutMs;
            this.InvokedTimeoutMs.Add(timeoutMs);
            this.LastInvokedCancellationToken = cancellationToken;

            // Simulate a slow/hung git process that only completes when the test signals it or the
            // caller cancels. This lets tests assert that cancellation actually interrupts an
            // in-flight credential invocation instead of blocking for the full timeout.
            ManualResetEventSlim blockUntilSignaled = this.BlockInvokeUntilSignaled;
            if (blockUntilSignaled != null)
            {
                this.InvokeReachedBlock?.Set();
                try
                {
                    blockUntilSignaled.Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Mirror the real GitProcess.InvokeGitImpl contract: a canceled invocation
                    // surfaces cancellation rather than returning a timeout Result.
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            if (this.ShouldFail)
            {
                return new Result(string.Empty, string.Empty, Result.GenericFailureCode);
            }

            Func<CommandInfo, bool> commandMatchFunction =
                (CommandInfo commandInfo) =>
                {
                    if (commandInfo.MatchPrefix)
                    {
                        return command.StartsWith(commandInfo.Command);
                    }
                    else
                    {
                        return string.Equals(command, commandInfo.Command, StringComparison.Ordinal);
                    }
                };

            CommandInfo matchedCommand = this.expectedCommandInfos.Last(commandMatchFunction);
            matchedCommand.ShouldNotBeNull("Unexpected command: " + command);

            var result = matchedCommand.Result();
            if (parseStdOutLine != null && !string.IsNullOrEmpty(result.Output))
            {
                using (StringReader reader = new StringReader(result.Output))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        parseStdOutLine(line);
                    }
                }
                /* Future: result.Output should be set to null in this case */
            }
            return result;
        }

        public class Credential
        {
            public Credential(string username, string password)
            {
                this.Username = username;
                this.Password = password;
            }

            public string Username { get; }
            public string Password { get; }

            public string BasicAuthString
            {
                get => Convert.ToBase64String(Encoding.ASCII.GetBytes(this.Username + ":" + this.Password));
            }
        }

        private class CommandInfo
        {
            public CommandInfo(string command, Func<Result> result, bool matchPrefix)
            {
                this.Command = command;
                this.Result = result;
                this.MatchPrefix = matchPrefix;
            }

            public string Command { get; private set; }

            public Func<Result> Result { get; private set; }

            public bool MatchPrefix { get; private set; }
        }
    }
}
