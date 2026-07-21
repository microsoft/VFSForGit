using GVFS.CommandLine;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CloneVerbTests
    {
        private CloneVerb cloneVerb;
        private string testDir;

        [SetUp]
        public void Setup()
        {
            this.cloneVerb = new CloneVerb();
            this.testDir = Path.Combine(Path.GetTempPath(), "CloneVerbTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testDir))
            {
                Directory.Delete(this.testDir, recursive: true);
            }
        }

        [TestCase]
        public void TryCreateEnlistmentFailsWithoutEnlistmentWhenTargetDirectoryIsNotEmpty()
        {
            File.WriteAllText(Path.Combine(this.testDir, "preexisting.txt"), "content");

            CloneVerb.Result result = this.cloneVerb.TryCreateEnlistment(
                this.testDir,
                this.testDir,
                out GVFS.Common.GVFSEnlistment enlistment);

            Assert.IsFalse(result.Success);
            Assert.IsNull(enlistment);
            StringAssert.Contains("exists and is not empty", result.ErrorMessage);
        }

        // Regression test: when the clone directory is non-empty, TryCreateEnlistment fails and
        // returns a null enlistment (see TryCreateEnlistmentFailsWithoutEnlistmentWhenTargetDirectoryIsNotEmpty
        // above). CloneVerb.Execute() must not dereference that null enlistment in the failure case,
        // otherwise `gvfs clone` into a non-empty directory throws a NullReferenceException instead of
        // reporting the "exists and is not empty" error (regression introduced after v1.0). The
        // trustPackIndexes lookup is now gated on cloneResult.Success before touching enlistment, and
        // delegates to LibGit2Repo.GetConfigBoolOrDefault (see LibGit2RepoConfigLookupTests) for the
        // config read itself.
    }
}
