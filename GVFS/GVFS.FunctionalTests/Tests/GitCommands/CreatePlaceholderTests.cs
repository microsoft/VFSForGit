using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class CreatePlaceholderTests : GitRepoTests
    {
        private static readonly string FileToRead = Path.Combine("GVFS", "GVFS", "Program.cs");

        public CreatePlaceholderTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase("check-attr --stdin --all")]
        [TestCase("check-ignore --stdin")]
        [TestCase("check-mailmap --stdin")]
        [TestCase("diff-tree --stdin")]
        [TestCase("hash-object --stdin")]
        [TestCase("index-pack --stdin")]
        [TestCase("name-rev --stdin")]
        [TestCase("rev-list --stdin --quiet --all")]
        [TestCase("update-ref --stdin")]
        public void AllowsPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: true);
        }

        [TestCase("checkout-index --stdin")]
        [TestCase("fetch-pack --stdin URL")]
        [TestCase("notes copy --stdin")]
        [TestCase("reset --stdin")]
        [TestCase("send-pack --stdin URL")]
        [TestCase("update-index --stdin")]
        [Category(Categories.WindowsOnly)] // Mac never blocks placeholder creation
        public void BlocksPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: false);
        }

        private void CheckPlaceholderCreation(string command, bool shouldAllow)
        {
            string eofCharacter = "\x04";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                eofCharacter = "\x1A";
            }

            this.EditFile($"Some new content for {command}.", "Protocol.md");
            ManualResetEventSlim resetEvent = GitHelpers.RunGitCommandWithWaitAndStdIn(this.Enlistment, resetTimeout: 3000, command: $"{command}", stdinToQuit: eofCharacter, processId: out _);

            if (shouldAllow)
            {
                this.FileContentsShouldMatch(FileToRead);
            }
            else
            {
                string virtualPath = Path.Combine(this.Enlistment.RepoRoot, FileToRead);
                string controlPath = Path.Combine(this.ControlGitRepo.RootPath, FileToRead);
                virtualPath.ShouldNotExistOnDisk(this.FileSystem);
                controlPath.ShouldBeAFile(this.FileSystem);
            }

            this.ValidateGitCommand("--no-optional-locks status");
            resetEvent.Wait();
            this.RunGitCommand("reset --hard");
        }
    }
}
