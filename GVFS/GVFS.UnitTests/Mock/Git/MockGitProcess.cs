using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGitProcess : GitProcess
    {
        private List<CommandInfo> expectedCommandInfos = new List<CommandInfo>();

        public MockGitProcess()
            : base(new MockGVFSEnlistment())
        {
        }

        public bool ShouldFail { get; set; }

        public void SetExpectedCommandResult(string command, Func<Result> result, bool matchPrefix = false)
        {
            CommandInfo commandInfo = new CommandInfo(command, result, matchPrefix);
            this.expectedCommandInfos.Add(commandInfo);
        }

        protected override Result InvokeGitImpl(string command, string workingDirectory, string dotGitDirectory, bool useReadObjectHook, Action<StreamWriter> writeStdIn, Action<string> parseStdOutLine, int timeoutMs)
        {
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
