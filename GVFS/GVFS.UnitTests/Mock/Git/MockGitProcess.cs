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
            this.Initialize();
        }

        public MockGitProcess(string gitBinPath, string workingDirectoryRoot)
            : base(gitBinPath, workingDirectoryRoot)
        {
            this.Initialize();
        }

        public List<string> CommandsRun { get; private set; }
        public bool ShouldFail { get; set; }
        public Dictionary<string, Credential> StoredCredentials { get; private set; }
        public Dictionary<string, List<Credential>> CredentialApprovals { get; private set; }
        public Dictionary<string, List<Credential>> CredentialRejections { get; private set; }

        /// <summary>
        /// The value passed as --git-dir for each invocation, in the order the
        /// invocations happened. An entry is null when no --git-dir was passed.
        /// </summary>
        public List<string> DotGitDirectoriesUsed { get; private set; }

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
            Action<string> parseStdOutToken = null)
        {
            this.CommandsRun.Add(command);
            this.DotGitDirectoriesUsed.Add(dotGitDirectory);

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

            if (parseStdOutToken != null && !string.IsNullOrEmpty(result.Output))
            {
                // Feed the mock output through the real production tokenizer so the test double cannot
                // drift from ReadStdOutTokens' actual semantics (empty records, trailing-fragment flush).
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(result.Output)))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    GitProcess.ReadStdOutTokens(reader, parseStdOutToken);
                }

                // In streaming mode production never subscribes OutputDataReceived, so Result.Output is
                // empty; mirror that here so callers cannot rely on Output being populated after streaming.
                result = new Result(string.Empty, result.Errors, result.ExitCode, result.OutputTruncated, result.ErrorsTruncated);
            }

            return result;
        }

        private void Initialize()
        {
            this.CommandsRun = new List<string>();
            this.DotGitDirectoriesUsed = new List<string>();
            this.StoredCredentials = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
            this.CredentialApprovals = new Dictionary<string, List<Credential>>();
            this.CredentialRejections = new Dictionary<string, List<Credential>>();
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
