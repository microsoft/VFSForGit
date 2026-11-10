using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    [NonParallelizable]
    public class GitTestHelperTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            this.root = Path.Combine(Path.GetTempPath(), nameof(GitTestHelperTests) + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(this.root, recursive: true);
        }

        [TestCase("First Git")]
        [TestCase("First; Git")]
        public void FindsTheFirstGitInQuotedPathEntriesWithSpaces(string firstDirectoryName)
        {
            string firstDirectory = Path.Combine(this.root, firstDirectoryName);
            string secondDirectory = Path.Combine(this.root, "Second Git");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);
            string expectedGit = Path.Combine(firstDirectory, "git.exe");
            File.WriteAllText(expectedGit, string.Empty);
            File.WriteAllText(Path.Combine(secondDirectory, "git.exe"), string.Empty);
            string searchPath = string.Join(Path.PathSeparator,
                Path.Combine(this.root, "missing"), "\"" + firstDirectory + "\"", secondDirectory);

            Assert.That(GitTestHelper.FindGitOnPath(searchPath), Is.EqualTo(expectedGit));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(";;")]
        [TestCase("\"\"; ;")]
        public void EmptyPathEntriesDoNotSearchTheWorkingDirectory(string searchPath)
        {
            File.WriteAllText(Path.Combine(this.root, "git.exe"), string.Empty);
            string originalDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = this.root;

                Assert.That(GitTestHelper.FindGitOnPath(searchPath), Is.Null);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        [Test]
        public void RelativePathEntriesReturnAnAbsolutePath()
        {
            string expectedGit = Path.Combine(this.root, "git.exe");
            File.WriteAllText(expectedGit, string.Empty);
            string searchPath = Path.GetRelativePath(Environment.CurrentDirectory, this.root);

            Assert.That(GitTestHelper.FindGitOnPath(searchPath), Is.EqualTo(expectedGit).IgnoreCase);
        }

        [Test]
        public void MissingGitReturnsNull()
        {
            Assert.That(GitTestHelper.FindGitOnPath(this.root), Is.Null);
        }
    }
}
