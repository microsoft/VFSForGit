using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using System;
using System.Collections.Generic;
using System.IO;
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
            this.StoredCredentials = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
            this.CredentialApprovals = new Dictionary<string, List<Credential>>();
            this.CredentialRejections = new Dictionary<string, List<Credential>>();
        }

        public List<string> CommandsRun { get; }
        public bool ShouldFail { get; set; }
        public Dictionary<string, Credential> StoredCredentials { get; }
        public Dictionary<string, List<Credential>> CredentialApprovals { get; }
        public Dictionary<string, List<Credential>> CredentialRejections { get; }

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
            string gitObjectsDirectory = null)
        {
            this.CommandsRun.Add(command);

            if (this.ShouldFail)
            {
                return new Result(string.Empty, string.Empty, Result.GenericFailureCode);
            }

            Predicate<CommandInfo> commandMatchFunction =
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

            CommandInfo matchedCommand = this.expectedCommandInfos.Find(commandMatchFunction);
            matchedCommand.ShouldNotBeNull("Unexpected command: " + command);

            return matchedCommand.Result();
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
