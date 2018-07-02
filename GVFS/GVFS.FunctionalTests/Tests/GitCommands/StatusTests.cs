using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class StatusTests : GitRepoTests
    {
        public StatusTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            string srcPath = @"Readme.md";
            string dstPath = Path.Combine(".git", "destination.txt");

            this.MoveFile(srcPath, dstPath);
            this.ValidateGitCommand("status");
        }
    }
}
