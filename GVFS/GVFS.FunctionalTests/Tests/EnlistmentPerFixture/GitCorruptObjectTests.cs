using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class GitCorruptObjectTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true to avoid any of the tests inadvertently corrupting
        // the cache
        public GitCorruptObjectTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public void GitRequestsReplacementForAllNullObject()
        {
            Action<string> allNullObject = (string objectPath) =>
            {
                FileInfo objectFileInfo = new FileInfo(objectPath);
                File.WriteAllBytes(objectPath, Enumerable.Repeat<byte>(0, (int)objectFileInfo.Length).ToArray());
            };

            this.RunGitDiffWithCorruptObject(allNullObject);
            this.RunGitCatFileWithCorruptObject(allNullObject);
            this.RunGitResetHardWithCorruptObject(allNullObject);
            this.RunGitCheckoutOnFileWithCorruptObject(allNullObject);
        }

        // TODO: This test no longer passes because Git fails on a truncated
        // object instead of clearing it and regenerating it over the GVFS
        // protocol.
        // [TestCase]
        public void GitRequestsReplacementForTruncatedObject()
        {
            Action<string> truncateObject = (string objectPath) =>
            {
                FileInfo objectFileInfo = new FileInfo(objectPath);
                using (FileStream objectStream = new FileStream(objectPath, FileMode.Open))
                {
                    objectStream.SetLength(objectFileInfo.Length - 8);
                }
            };

            this.RunGitDiffWithCorruptObject(truncateObject);

            // TODO 1114508: Update git cat-file to request object from GVFS when it finds a truncated object on disk.
            ////this.RunGitCatFileWithCorruptObject(truncateObject);

            this.RunGitResetHardWithCorruptObject(truncateObject);
            this.RunGitCheckoutOnFileWithCorruptObject(truncateObject);
        }

        [TestCase]
        public void GitRequestsReplacementForObjectCorruptedWithBadData()
        {
            Action<string> fillObjectWithBadData = (string objectPath) =>
            {
                this.fileSystem.WriteAllText(objectPath, "Not a valid git object");
            };

            this.RunGitDiffWithCorruptObject(fillObjectWithBadData);
            this.RunGitCatFileWithCorruptObject(fillObjectWithBadData);
            this.RunGitResetHardWithCorruptObject(fillObjectWithBadData);
            this.RunGitCheckoutOnFileWithCorruptObject(fillObjectWithBadData);
        }

        private void RunGitDiffWithCorruptObject(Action<string> corruptObject)
        {
            string fileName = "Protocol.md";
            string filePath = this.Enlistment.GetVirtualPathTo(fileName);
            string fileContents = filePath.ShouldBeAFile(this.fileSystem).WithContents();
            string newFileContents = "RunGitDiffWithCorruptObject";
            this.fileSystem.WriteAllText(filePath, newFileContents);

            string sha;
            string objectPath = this.GetLooseObjectPath(fileName, out sha);
            corruptObject(objectPath);

            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"diff {fileName}");
            revParseResult.ExitCode.ShouldEqual(0);
            revParseResult.Output.ShouldContain("The GVFS network protocol consists of three operations");
            revParseResult.Output.ShouldContain(newFileContents);
        }

        private void RunGitCatFileWithCorruptObject(Action<string> corruptObject)
        {
            string fileName = "Readme.md";
            string filePath = this.Enlistment.GetVirtualPathTo(fileName);
            string fileContents = filePath.ShouldBeAFile(this.fileSystem).WithContents();

            string sha;
            string objectPath = this.GetLooseObjectPath(fileName, out sha);
            corruptObject(objectPath);

            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"cat-file blob {sha}");
            revParseResult.ExitCode.ShouldEqual(0);
            revParseResult.Output.ShouldEqual(fileContents);
        }

        private void RunGitResetHardWithCorruptObject(Action<string> corruptObject)
        {
            string fileName = "Readme.md";
            string filePath = this.Enlistment.GetVirtualPathTo(fileName);
            string fileContents = filePath.ShouldBeAFile(this.fileSystem).WithContents();
            string newFileContents = "RunGitDiffWithCorruptObject";
            this.fileSystem.WriteAllText(filePath, newFileContents);

            string sha;
            string objectPath = this.GetLooseObjectPath(fileName, out sha);
            corruptObject(objectPath);

            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reset --hard HEAD");
            revParseResult.ExitCode.ShouldEqual(0);
            filePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        private void RunGitCheckoutOnFileWithCorruptObject(Action<string> corruptObject)
        {
            string fileName = "Readme.md";
            string filePath = this.Enlistment.GetVirtualPathTo(fileName);
            string fileContents = filePath.ShouldBeAFile(this.fileSystem).WithContents();
            string newFileContents = "RunGitDiffWithCorruptObject";
            this.fileSystem.WriteAllText(filePath, newFileContents);

            string sha;
            string objectPath = this.GetLooseObjectPath(fileName, out sha);
            corruptObject(objectPath);

            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"checkout -- {fileName}");
            revParseResult.ExitCode.ShouldEqual(0);
            filePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        private string GetLooseObjectPath(string fileGitPath, out string sha)
        {
            ProcessResult revParseResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"rev-parse :{fileGitPath}");
            sha = revParseResult.Output.Trim();
            if (FileSystemHelpers.CaseSensitiveFileSystem)
            {
                // Ensure SHA path is lowercase for case-sensitive filesystems
                sha = sha.ToLower();
            }

            sha.Length.ShouldEqual(40);
            string objectPath = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), sha.Substring(0, 2), sha.Substring(2, 38));
            return objectPath;
        }
    }
}
