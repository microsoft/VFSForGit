using GVFS.Common;
using GVFS.RepairJobs;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class GitConfigRepairJobTests
    {
        private string testEnlistmentRoot;
        private string dotGitRoot;

        [SetUp]
        public void CreateTestEnlistmentRoot()
        {
            this.testEnlistmentRoot = Path.Combine(Path.GetTempPath(), "GVFSUnitTests_GitConfigRepairJob_" + Guid.NewGuid().ToString("N"));

            // Construct a real (non-mock) GVFSEnlistment pointed at a scratch directory purely
            // to get DotGitRoot's platform-specific layout right - PackedRefsLookSha256 only
            // does real file I/O under that path, so there is no need to mock GitProcess or
            // register GVFSPlatform for these tests specifically (though the [SetUpFixture] in
            // GVFS.UnitTests.Setup must still have run, as it does for the whole assembly).
            GVFSEnlistment enlistment = new GVFSEnlistment(this.testEnlistmentRoot, "mock://repoUrl", "mock:\\git.exe", authentication: null);
            this.dotGitRoot = enlistment.DotGitRoot;
            Directory.CreateDirectory(this.dotGitRoot);
        }

        [TearDown]
        public void DeleteTestEnlistmentRoot()
        {
            if (Directory.Exists(this.testEnlistmentRoot))
            {
                Directory.Delete(this.testEnlistmentRoot, recursive: true);
            }
        }

        [TestCase]
        public void PackedRefsLookSha256IsTrueForSha256WidthObjectId()
        {
            this.WritePackedRefs("bb3fd16685b64a36d8d433fd0684d192277bb3b64f2b8441c5832d8d3ba8da91 refs/heads/master");

            this.CreateJob().PackedRefsLookSha256().ShouldEqual(true);
        }

        [TestCase]
        public void PackedRefsLookSha256IsFalseForSha1WidthObjectId()
        {
            this.WritePackedRefs("a3541ccec5694b0334d91298e348dc7401c07ff7 refs/heads/master");

            this.CreateJob().PackedRefsLookSha256().ShouldEqual(false);
        }

        [TestCase]
        public void PackedRefsLookSha256SkipsHeaderCommentAndPeeledLines()
        {
            // Matches the real shape 'git pack-refs'/GitRefs.ToPackedRefs produce: a leading
            // '#' comment, and '^'-prefixed peeled object ids for annotated tags that must not
            // be mistaken for the preceding ref's own object id.
            this.WritePackedRefs(
                "# pack-refs with: peeled fully-peeled",
                "bb3fd16685b64a36d8d433fd0684d192277bb3b64f2b8441c5832d8d3ba8da91 refs/tags/v1",
                "^0000000000000000000000000000000000000000000000000000000000000000");

            this.CreateJob().PackedRefsLookSha256().ShouldEqual(true);
        }

        [TestCase]
        public void PackedRefsLookSha256IsFalseWhenFileIsMissing()
        {
            this.CreateJob().PackedRefsLookSha256().ShouldEqual(false);
        }

        private GitConfigRepairJob CreateJob()
        {
            GVFSEnlistment enlistment = new GVFSEnlistment(this.testEnlistmentRoot, "mock://repoUrl", "mock:\\git.exe", authentication: null);
            return new GitConfigRepairJob(new MockTracer(), new StringWriter(), enlistment);
        }

        private void WritePackedRefs(params string[] lines)
        {
            File.WriteAllLines(Path.Combine(this.dotGitRoot, GVFSConstants.DotGit.PackedRefsName), lines);
        }
    }
}
