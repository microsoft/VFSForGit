using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    [Category(Categories.Mac.M3)]
    public class AddStageTests : GitRepoTests
    {
        public AddStageTests() : base(enlistmentPerTest: false)
        {
        }

        [TestCase, Order(1)]
        [Category(Categories.Mac.M2)]
        public void AddBasicTest()
        {
            this.EditFile("Readme.md", "Some new content.");
            this.ValidateGitCommand("add Readme.md");
            this.RunGitCommand("commit -m \"Changing the Readme.md\"");
        }

        [TestCase, Order(2)]
        [Category(Categories.Mac.M2)]
        public void StageBasicTest()
        {
            this.EditFile("AuthoringTests.md", "Some new content.");
            this.ValidateGitCommand("stage AuthoringTests.md");
            this.RunGitCommand("commit -m \"Changing the AuthoringTests.md\"");
        }

        [TestCase, Order(3)]
        public void AddAllowsPlaceholderCreation()
        {
            this.CommandAllowsPlaceholderCreation("add", @"GVFS\GVFS\Program.cs");
        }

        [TestCase, Order(4)]
        public void StageAllowsPlaceholderCreation()
        {
            this.CommandAllowsPlaceholderCreation("stage", @"GVFS\GVFS\App.config");
        }

        private void CommandAllowsPlaceholderCreation(string command, string fileToRead)
        {
            this.EditFile("Readme.md", $"Some new content for {command}.");
            ManualResetEventSlim resetEvent = GitHelpers.RunGitCommandWithWaitAndStdIn(this.Enlistment, resetTimeout: 3000, command: $"{command} -p", stdinToQuit: "q", processId: out _);
            this.FileContentsShouldMatch(fileToRead);
            this.ValidateGitCommand("--no-optional-locks status");
            resetEvent.Wait();
            this.RunGitCommand("reset --hard");
        }
    }
}
