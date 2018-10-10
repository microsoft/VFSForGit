using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class CreatePlaceholderTests : GitRepoTests
    {
        private static readonly string FileToRead = Path.Combine("GVFS", "GVFS", "Program.cs");

        public CreatePlaceholderTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase("check-attr")]
        [TestCase("check-ignore")]
        [TestCase("check-mailmap")]
        [TestCase("diff-tree")]
        [TestCase("fetch-pack")]
        [TestCase("hash-object")]
        [TestCase("index-pack")]
        [TestCase("name-rev")]
        [TestCase("notes")]
        [TestCase("send-pack")]
        [TestCase("rev-list")]
        [TestCase("update-ref")]
        public void AllowsPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: true);
        }

        [TestCase("checkout-index")]
        [TestCase("reset")]
        [TestCase("update-index")]
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
            ManualResetEventSlim resetEvent = GitHelpers.RunGitCommandWithWaitAndStdIn(this.Enlistment, resetTimeout: 3000, command: $"{command} --stdin", stdinToQuit: eofCharacter, processId: out _);

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
