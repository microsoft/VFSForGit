using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGitProcess : GitProcess
    {
        private List<CommandInfo> expectedCommandInfos = new List<CommandInfo>();

        public MockGitProcess()
            : base(new MockGVFSEnlistment())
        {
            this.CommandsRun = new List<string>();
            this.InvocationsRun = new List<GitInvocation>();
            this.StoredCredentials = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
            this.CredentialApprovals = new Dictionary<string, List<Credential>>();
            this.CredentialRejections = new Dictionary<string, List<Credential>>();
        }

        public List<string> CommandsRun { get; }

        /// <summary>
        /// Records every call into <see cref="InvokeGitImpl"/> in order, with the mayRequireAuth and
        /// hasVisibleConsoleWindow values passed to it. Recording each invocation (rather than keeping only
        /// the last value per command) keeps the wiring assertions honest if a command is ever run more than
        /// once with different flags.
        /// </summary>
        public List<GitInvocation> InvocationsRun { get; }
        public bool ShouldFail { get; set; }
        public Dictionary<string, Credential> StoredCredentials { get; }
        public Dictionary<string, List<Credential>> CredentialApprovals { get; }
        public Dictionary<string, List<Credential>> CredentialRejections { get; }

        /// <summary>
        /// Returns the mayRequireAuth value the given command was invoked with, asserting the command ran and
        /// that every invocation of it agreed on the value.
        /// </summary>
        public bool MayRequireAuthFor(string command)
        {
            List<GitInvocation> matches = this.InvocationsRun
                .Where(invocation => string.Equals(invocation.Command, command, StringComparison.Ordinal))
                .ToList();

            matches.Count.ShouldBeAtLeast(1, "Command was never run: " + command);
            matches.Select(invocation => invocation.MayRequireAuth).Distinct().Count()
                .ShouldEqual(1, "Command ran with conflicting mayRequireAuth values: " + command);

            return matches[0].MayRequireAuth;
        }

        public void SetExpectedCommandResult(string command, Func<Result> result, bool matchPrefix = false)
        {
            CommandInfo commandInfo = new CommandInfo(command, result, matchPrefix);
            this.expectedCommandInfos.Add(commandInfo);
        }

        public override bool TryStoreCredential(ITracer tracer, string repoUrl, string username, string password, out string error)
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

            return base.TryStoreCredential(tracer, repoUrl, username, password, out error);
        }

        public override bool TryDeleteCredential(ITracer tracer, string repoUrl, string username, string password, out string error)
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

            return base.TryDeleteCredential(tracer, repoUrl, username, password, out error);
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
            bool mayRequireAuth = false,
            bool? hasVisibleConsoleWindow = null)
        {
            this.CommandsRun.Add(command);
            this.InvocationsRun.Add(new GitInvocation(command, mayRequireAuth, hasVisibleConsoleWindow));

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

        public class GitInvocation
        {
            public GitInvocation(string command, bool mayRequireAuth, bool? hasVisibleConsoleWindow)
            {
                this.Command = command;
                this.MayRequireAuth = mayRequireAuth;
                this.HasVisibleConsoleWindow = hasVisibleConsoleWindow;
            }

            public string Command { get; }
            public bool MayRequireAuth { get; }
            public bool? HasVisibleConsoleWindow { get; }
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
