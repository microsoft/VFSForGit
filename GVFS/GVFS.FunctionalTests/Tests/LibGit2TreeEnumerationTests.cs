using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GitProcess = GVFS.FunctionalTests.Tools.GitProcess;
using ProcessResult = GVFS.FunctionalTests.Tools.ProcessResult;

namespace GVFS.FunctionalTests.Tests
{
    /// <summary>
    /// Tests <see cref="LibGit2Repo.EnumerateTree"/> with the real libgit2 (git2.dll). The
    /// projection uses it to read the trees behind the sparse-directory entries of a sparse index.
    /// </summary>
    [TestFixture]
    public class LibGit2TreeEnumerationTests
    {
        // "café.txt", from a \u escape so that the test does not depend on the encoding of this file
        private const string NonAsciiName = "caf\u00e9.txt";

        private string repoRoot;
        private string rootTree;
        private string blob;
        private string emptyTree;

        [OneTimeSetUp]
        public void CreateRepo()
        {
            this.repoRoot = Path.Combine(Path.GetTempPath(), "GVFS.LibGit2TreeEnumerationTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(this.repoRoot);
            GitProcess.Invoke(this.repoRoot, "init");

            this.blob = this.GitWithInput("hash-object -w --stdin", "hello\n");
            string script = this.GitWithInput("hash-object -w --stdin", "#!/bin/sh\n");
            this.emptyTree = this.GitWithInput("hash-object -t tree -w --stdin", string.Empty);
            string subtree = this.GitWithInput("mktree", $"100644 blob {this.blob}\tb.txt\n");
            this.rootTree = this.GitWithInput(
                "mktree",
                $"100644 blob {this.blob}\ta.txt\n" +
                $"100755 blob {script}\trun.sh\n" +
                $"120000 blob {this.blob}\tlink\n" +
                $"160000 commit {new string('1', 40)}\tmodule\n" +
                $"040000 tree {subtree}\tsub\n" +
                $"040000 tree {this.emptyTree}\tempty\n" +
                $"100644 blob {this.blob}\t{NonAsciiName}\n");
        }

        [OneTimeTearDown]
        public void DeleteRepo()
        {
            if (this.repoRoot != null)
            {
                RepositoryHelpers.DeleteTestDirectory(this.repoRoot);
            }
        }

        [TestCase]
        public void EnumerateTreeReturnsEveryEntryInTreeOrder()
        {
            List<string> expected = GitProcess.Invoke(this.repoRoot, "-c core.quotepath=false ls-tree " + this.rootTree)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t'))
                .Select(parts => parts[0].Split(' ')[0] + " " + parts[0].Split(' ')[2] + " " + parts[1])
                .ToList();
            expected.Count.ShouldEqual(7);

            List<string> actual = new List<string>();
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                repo.EnumerateTree(SHA1Util.BytesFromHexString(this.rootTree), (name, objectId, fileMode) =>
                {
                    actual.Add(
                        Convert.ToString(fileMode, 8).PadLeft(6, '0') + " " +
                        SHA1Util.HexStringFromBytes(objectId.ToArray()).ToLowerInvariant() + " " +
                        Encoding.UTF8.GetString(name));
                }).ShouldEqual(TreeEnumerationResult.Success);
            }

            actual.ShouldMatchInOrder(expected);
            actual.ShouldContain(line => line.EndsWith(" " + NonAsciiName));
        }

        [TestCase]
        public void EnumerateTreeReportsMissingTreeAndNonTree()
        {
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                int calls = 0;
                TreeEntryVisitor visitor = (name, objectId, fileMode) => calls++;

                repo.EnumerateTree(SHA1Util.BytesFromHexString(new string('2', 40)), visitor).ShouldEqual(TreeEnumerationResult.MissingTree);
                repo.EnumerateTree(SHA1Util.BytesFromHexString(this.blob), visitor).ShouldEqual(TreeEnumerationResult.NotTree);
                repo.EnumerateTree(SHA1Util.BytesFromHexString(this.emptyTree), visitor).ShouldEqual(TreeEnumerationResult.Success);
                calls.ShouldEqual(0);
            }
        }

        private string GitWithInput(string command, string input)
        {
            using (MemoryStream stream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(input)))
            {
                ProcessResult result = GitProcess.InvokeProcess(this.repoRoot, command, inputStream: stream);
                result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
                return result.Output.Trim();
            }
        }
    }
}
