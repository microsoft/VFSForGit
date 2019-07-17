using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class AddStageTests : GitRepoTests
    {
        public AddStageTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: false, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase, Order(1)]
        public void AddBasicTest()
        {
            this.EditFile("Some new content.", "Readme.md");
            this.ValidateGitCommand("add Readme.md");
            this.RunGitCommand("commit -m \"Changing the Readme.md\"");
        }

        [TestCase, Order(2)]
        public void StageBasicTest()
        {
            this.EditFile("Some new content.", "AuthoringTests.md");
            this.ValidateGitCommand("stage AuthoringTests.md");
            this.RunGitCommand("commit -m \"Changing the AuthoringTests.md\"");
        }

        [TestCase, Order(3)]
        public void AddAndStageHardLinksTest()
        {
            this.CreateHardLink("ReadmeLink.md", "Readme.md");
            this.ValidateGitCommand("add ReadmeLink.md");
            this.RunGitCommand("commit -m \"Created ReadmeLink.md\"");

            this.CreateHardLink("AuthoringTestsLink.md", "AuthoringTests.md");
            this.ValidateGitCommand("stage AuthoringTestsLink.md");
            this.RunGitCommand("commit -m \"Created AuthoringTestsLink.md\"");
        }

        [TestCase, Order(4)]
        public void AddAllowsPlaceholderCreation()
        {
            this.CommandAllowsPlaceholderCreation("add", "GVFS", "GVFS", "Program.cs");
        }

        [TestCase, Order(5)]
        public void StageAllowsPlaceholderCreation()
        {
            this.CommandAllowsPlaceholderCreation("stage", "GVFS", "GVFS", "App.config");
        }

        private void CommandAllowsPlaceholderCreation(string command, params string[] fileToReadPathParts)
        {
            string fileToRead = Path.Combine(fileToReadPathParts);
            this.EditFile($"Some new content for {command}.", "Protocol.md");
            ManualResetEventSlim resetEvent = GitHelpers.RunGitCommandWithWaitAndStdIn(this.Enlistment, resetTimeout: 3000, command: $"{command} -p", stdinToQuit: "q", processId: out _);
            this.FileContentsShouldMatch(fileToRead);
            this.ValidateGitCommand("--no-optional-locks status");
            resetEvent.Wait();
            this.RunGitCommand("reset --hard");
        }
    }
}
